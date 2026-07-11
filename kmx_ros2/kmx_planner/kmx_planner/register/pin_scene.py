#!/usr/bin/env python3
"""Reusable pin+coal collision/distance oracle for CRX-30iA (register redesign 土台).

PinScene loads the clean URDF, applies the SRDF ACM, and syncs world obstacles +
attached (head) boxes from a moveit_msgs/PlanningScene into coal geometry. It then
answers, for a joint config q (radians, [J1..J6]):

    collides(q)      -> bool   (any active pair in collision)
    first_collision  -> bool   (stop-at-first, faster gate)
    min_distance(q)  -> (dist_m, (nameA, nameB))   signed sep; <0 => penetration
    pair_distances(q)-> list[(dist, nameA, nameB)]

This is the collision truth used by STOMP-lite (②) and re-timing (③). Verified to
match MoveIt /check_state_validity 1:1 (see parity_test.py). NOTE: pin uses zero
padding; MoveIt applies ~0.01 m link padding, so treat clearances < ~15 mm as
"MoveIt would call this a contact" — the publish-time /check_state_validity gate is
still the final authority.
"""
import os
import numpy as np
import pinocchio as pin
import coal
from shape_msgs.msg import SolidPrimitive

HERE = os.path.dirname(os.path.abspath(__file__))
URDF = os.path.join(HERE, "crx30ia_clean.urdf")
SRDF = "/home/kyotoss/ros2_ws/src/fanuc_driver/fanuc_moveit_config/srdf/crx30ia.srdf"
PKG = ["/home/kyotoss/ros2_ws/install/fanuc_crx_description/share"]
JOINTS = ["J1", "J2", "J3", "J4", "J5", "J6"]


def se3_from_pose(p):
    q = pin.Quaternion(p.orientation.w, p.orientation.x, p.orientation.y, p.orientation.z)
    q.normalize()
    return pin.SE3(q.matrix(), np.array([p.position.x, p.position.y, p.position.z]))


def coal_from_primitive(prim):
    if prim.type == SolidPrimitive.BOX:
        return coal.Box(prim.dimensions[0], prim.dimensions[1], prim.dimensions[2])
    if prim.type == SolidPrimitive.SPHERE:
        return coal.Sphere(prim.dimensions[0])
    if prim.type == SolidPrimitive.CYLINDER:
        return coal.Cylinder(prim.dimensions[1], prim.dimensions[0])  # radius, length
    return None


class PinScene:
    def __init__(self, urdf=URDF, srdf=SRDF, pkg=PKG):
        self.model = pin.buildModelFromUrdf(urdf)
        self.gm = pin.buildGeomFromUrdf(self.model, urdf, pin.GeometryType.COLLISION, pkg)
        self.gm.addAllCollisionPairs()
        pin.removeCollisionPairs(self.model, self.gm, srdf)
        self.n_self_pairs = len(self.gm.collisionPairs)
        self.robot_geom_idx = {g.name[:-2] if g.name.endswith("_0") else g.name: i
                               for i, g in enumerate(self.gm.geometryObjects)}
        self.robot_geoms = list(range(len(self.gm.geometryObjects)))
        self.world_geoms = []
        self.n_world = self.n_attached = 0
        self.data = None
        self.gd = None
        # joint limits (rad), nudged off exact bounds
        self.qmin = self.model.lowerPositionLimit.copy()
        self.qmax = self.model.upperPositionLimit.copy()

    def _add_geom(self, name, parent_joint, geom, placement):
        return self.gm.addGeometryObject(
            pin.GeometryObject(name, parent_joint, placement, geom))

    def sync_from_scene(self, scene):
        """scene = moveit_msgs/PlanningScene. Adds world + attached geoms & pairs."""
        for co in scene.world.collision_objects:
            objT = se3_from_pose(co.pose)
            for k, prim in enumerate(co.primitives):
                g = coal_from_primitive(prim)
                if g is None:
                    continue
                gi = self._add_geom(f"world::{co.id}::{k}", 0, g,
                                    objT * se3_from_pose(co.primitive_poses[k]))
                self.world_geoms.append(gi)
                for rj in self.robot_geoms:
                    self.gm.addCollisionPair(pin.CollisionPair(rj, gi))
                self.n_world += 1
        for ao in scene.robot_state.attached_collision_objects:
            fid = self.model.getFrameId(ao.link_name)
            frame = self.model.frames[fid]
            pj, fplace = frame.parentJoint, frame.placement
            skip = set(ao.touch_links) | {ao.link_name}
            co = ao.object
            objT = se3_from_pose(co.pose)
            for k, prim in enumerate(co.primitives):
                g = coal_from_primitive(prim)
                if g is None:
                    continue
                gi = self._add_geom(f"att::{co.id}::{k}", pj, g,
                                    fplace * objT * se3_from_pose(co.primitive_poses[k]))
                for lname, rj in self.robot_geom_idx.items():
                    if lname not in skip:
                        self.gm.addCollisionPair(pin.CollisionPair(rj, gi))
                for wj in self.world_geoms:
                    self.gm.addCollisionPair(pin.CollisionPair(gi, wj))
                self.n_attached += 1

    def finalize(self):
        self.data = self.model.createData()
        self.gd = self.gm.createData()          # margin 0 -> hard feasibility & exact distance
        return self

    # ---- clearance oracle (nested security margins; see profiling notes) ----
    # computeDistances (exact) is ~15ms/config; computeCollisions with a security
    # margin is ~0.2ms and its isCollision boolean is parity-reliable. So grade
    # clearance by a few pre-configured margin levels instead of exact distance.
    def setup_clearance(self, d_safe=0.03, levels=(0.010, 0.020, 0.030)):
        self.d_safe = d_safe
        self.levels = sorted(levels)
        assert abs(self.levels[-1] - d_safe) < 1e-9, "largest level must equal d_safe"
        npair = len(self.gm.collisionPairs)
        self._gd_lvl = []
        for L in self.levels:
            gdl = self.gm.createData()
            for k in range(npair):
                gdl.collisionRequests[k].security_margin = L
            self._gd_lvl.append(gdl)
        return self

    def _within(self, q, gdl):
        return pin.computeCollisions(self.model, self.data, self.gm, gdl, q, True)

    def clearance_soft(self, q):
        """Fast quantized clearance. Returns (deficit_m, feasible).
        deficit = max(0, d_safe - clearance_bucket); feasible=False if hard collision.
        clearance_bucket in {0, levels...}; deficit=0 when clearance >= d_safe."""
        q = np.asarray(q)
        # coarse: anything within d_safe at all?
        if not self._within(q, self._gd_lvl[-1]):
            return 0.0, True                        # clearance >= d_safe
        if pin.computeCollisions(self.model, self.data, self.gm, self.gd, q, True):
            return self.d_safe, False               # hard collision
        clear = 0.0
        for i, L in enumerate(self.levels):
            if self._within(q, self._gd_lvl[i]):
                clear = self.levels[i - 1] if i > 0 else 0.0
                break
        return max(0.0, self.d_safe - clear), True

    def clearance_exact(self, q):
        """High-quality clearance via exact distances (~15ms). Returns (deficit_m, feasible)."""
        dmin, _ = self.min_distance(q)
        feasible = dmin > 1e-6
        deficit = max(0.0, self.d_safe - dmin) if feasible else self.d_safe
        return deficit, feasible

    def _names(self, k):
        cp = self.gm.collisionPairs[k]
        return self.gm.geometryObjects[cp.first].name, self.gm.geometryObjects[cp.second].name

    def first_collision(self, q):
        return pin.computeCollisions(self.model, self.data, self.gm, self.gd, np.asarray(q), True)

    def collides(self, q):
        pin.computeCollisions(self.model, self.data, self.gm, self.gd, np.asarray(q), False)
        return any(self.gd.collisionResults[k].isCollision()
                   for k in range(len(self.gm.collisionPairs)))

    def colliding_pairs(self, q):
        pin.computeCollisions(self.model, self.data, self.gm, self.gd, np.asarray(q), False)
        return [self._names(k) for k in range(len(self.gm.collisionPairs))
                if self.gd.collisionResults[k].isCollision()]

    def min_distance(self, q):
        pin.computeDistances(self.model, self.data, self.gm, self.gd, np.asarray(q))
        best, bp = 1e9, ("", "")
        for k in range(len(self.gm.collisionPairs)):
            d = self.gd.distanceResults[k].min_distance
            if d < best:
                best, bp = d, self._names(k)
        return best, bp

    def summary(self):
        return (f"pairs: self={self.n_self_pairs} +world={self.n_world} "
                f"+attached={self.n_attached} total={len(self.gm.collisionPairs)}")
