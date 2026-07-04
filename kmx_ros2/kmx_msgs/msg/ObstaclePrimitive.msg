# kmx_msgs/ObstaclePrimitive
# 1つの障害物プリミティブ。ロボット基部(base_link)相対・メートル・ROS(右手Z-up)系。
string id                 # 一意ID（Unityオブジェクト名+InstanceID など）
uint8 type                # 1=BOX, 2=SPHERE, 3=CYLINDER（shape_msgs/SolidPrimitive 準拠）
float64[] dimensions      # BOX:[x,y,z] / SPHERE:[radius] / CYLINDER:[height,radius]
geometry_msgs/Pose pose   # base_link 相対の姿勢
