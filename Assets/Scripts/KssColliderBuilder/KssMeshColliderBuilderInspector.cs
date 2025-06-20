using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using ShapeType = SAColliderBuilderCommon.ShapeType;
using MeshType = SAColliderBuilderCommon.MeshType;
using SliceMode = SAColliderBuilderCommon.SliceMode;
using ElementType = SAColliderBuilderCommon.ElementType;
using SplitMesh = SAMeshColliderCommon.SplitMesh;
using SplitMode = SAMeshColliderCommon.SplitMode;
using MeshCache = KssMeshColliderEditorCommon.MeshCache;
using ReducerTask = KssMeshColliderEditorCommon.ReducerTask;

using ReducerProperty = SAColliderBuilderCommon.ReducerProperty;
using ColliderProperty = SAColliderBuilderCommon.ColliderProperty;
using RigidbodyProperty = SAColliderBuilderCommon.RigidbodyProperty;

using ReducerOption = KssColliderBuilderEditorCommon.ReducerOption;
using ColliderOption = KssColliderBuilderEditorCommon.ColliderOption;
using SplitProperty = SAMeshColliderCommon.SplitProperty;
using SAMeshColliderProperty = SAMeshColliderCommon.SAMeshColliderProperty;
using SAMeshColliderBuilderProperty = SAMeshColliderCommon.SAMeshColliderBuilderProperty;

public static class KssMeshColliderBuilderInspector
{

    public static void Process(SAMeshColliderBuilder meshColliderBuilder)
    {
        if (meshColliderBuilder == null)
        {
            Debug.LogError("");
            return;
        }

        MeshFilter[] meshFilters = KssColliderBuilderEditorCommon.GetMeshFilters(meshColliderBuilder.gameObject);
        SkinnedMeshRenderer[] skinnedMeshRenderers = KssColliderBuilderEditorCommon.GetSkinnedMeshRenderers(meshColliderBuilder.gameObject);

        if ((meshFilters == null || meshFilters.Length == 0) && (skinnedMeshRenderers == null || skinnedMeshRenderers.Length == 0))
        {
            Debug.LogError("Nothing MeshFilter/SkinnedMeshRenderer. Skip Processing.");
            return;
        }

        List<ReducerTask> reducerTasks = new List<ReducerTask>();

        if (meshFilters != null)
        {
            foreach (MeshFilter meshFilter in meshFilters)
            {
                Mesh mesh = KssColliderBuilderEditorCommon.GetMesh(meshFilter);
                Material[] materials = KssColliderBuilderEditorCommon.GetMaterials(meshFilter);
                MeshCache meshCahce = new MeshCache(mesh, materials);
                KssMeshColliderEditorCommon.CleanupChildSAMeshColliders(meshFilter.gameObject, meshColliderBuilder.cleanupModified);
                ProcessRoot(reducerTasks, meshCahce, meshColliderBuilder, meshFilter.gameObject);
            }
        }

        if (skinnedMeshRenderers != null)
        {
            foreach (SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
            {
                Mesh mesh = KssColliderBuilderEditorCommon.GetMesh(skinnedMeshRenderer);
                Material[] materials = KssColliderBuilderEditorCommon.GetMaterials(skinnedMeshRenderer);
                MeshCache meshCahce = new MeshCache(mesh, materials);
                KssMeshColliderEditorCommon.CleanupChildSAMeshColliders(skinnedMeshRenderer.gameObject, meshColliderBuilder.cleanupModified);
                ProcessRoot(reducerTasks, meshCahce, meshColliderBuilder, skinnedMeshRenderer.gameObject);
            }
        }

        KssMeshColliderEditorCommon.Reduce(reducerTasks, meshColliderBuilder.isDebug);
    }

    static void Cleanup(SAMeshColliderBuilder meshColliderBuilder)
    {
        if (meshColliderBuilder == null)
        {
            Debug.LogError("");
            return;
        }

        MeshFilter[] meshFilters = KssColliderBuilderEditorCommon.GetMeshFilters(meshColliderBuilder.gameObject);
        SkinnedMeshRenderer[] skinnedMeshRenderers = KssColliderBuilderEditorCommon.GetSkinnedMeshRenderers(meshColliderBuilder.gameObject);

        if ((meshFilters == null || meshFilters.Length == 0) && (skinnedMeshRenderers == null || skinnedMeshRenderers.Length == 0))
        {
            Debug.LogError("Nothing MeshFilter/SkinnedMeshRenderer. Skip Cleanuping.");
            return;
        }

        if (meshFilters != null)
        {
            foreach (MeshFilter meshFilter in meshFilters)
            {
                KssMeshColliderEditorCommon.CleanupChildSAMeshColliders(meshFilter.gameObject, meshColliderBuilder.cleanupModified);
            }
        }

        if (skinnedMeshRenderers != null)
        {
            foreach (SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
            {
                KssMeshColliderEditorCommon.CleanupChildSAMeshColliders(skinnedMeshRenderer.gameObject, meshColliderBuilder.cleanupModified);
            }
        }
    }

    static void ProcessRoot(List<ReducerTask> reducerTasks, MeshCache meshCache, SAMeshColliderBuilder meshColliderBuilder, GameObject parentGameObject)
    {
        if (reducerTasks == null || meshCache == null || meshColliderBuilder == null || parentGameObject == null)
        {
            Debug.LogError("");
            return;
        }

        SplitMesh resplitMesh = KssMeshColliderEditorCommon.MakeRootSplitMesh(meshCache);
        if (resplitMesh == null)
        {
            return;
        }

        if (meshColliderBuilder.splitMaterialEnabled)
        {
            ProcessMaterial(reducerTasks, meshCache, meshColliderBuilder, parentGameObject);
        }
        else if (meshColliderBuilder.splitPrimitiveEnabled)
        {
            ProcessPrimitive(reducerTasks, meshCache, meshColliderBuilder, parentGameObject, resplitMesh);
        }
        else if (meshColliderBuilder.splitPolygonNormalEnabled)
        {
            KssMeshColliderEditorCommon.MakeSplitMeshTriangles(meshCache, resplitMesh);
            ProcessPolygon(reducerTasks, meshCache, meshColliderBuilder, parentGameObject, resplitMesh);
        }
        else
        {
            SAMeshCollider[] existingMeshColliders = KssMeshColliderEditorCommon.GetChildSAMeshColliders(parentGameObject);
            SAMeshCollider existingMeshCollider = KssMeshColliderEditorCommon.FindSAMeshCollider(existingMeshColliders, resplitMesh);
            if (existingMeshCollider != null && existingMeshCollider.modified)
            {
                return; // Not overwrite modified SAMeshCollider.
            }

            string resplitMeshColliderName = KssMeshColliderEditorCommon.GetSAMeshColliderName_Root(parentGameObject);
            SAMeshCollider resplitMeshCollider = null;
            if (existingMeshCollider != null)
            {
                resplitMeshCollider = existingMeshCollider;
                KssMeshColliderEditorCommon.SetupSAMeshCollider(meshColliderBuilder, resplitMeshCollider, resplitMeshColliderName);
                resplitMesh = resplitMeshCollider.splitMesh;
            }
            else
            {
                resplitMeshCollider = KssMeshColliderEditorCommon.CreateSAMeshCollider(meshColliderBuilder, parentGameObject, resplitMeshColliderName, resplitMesh, SplitMode.None);
            }

            KssMeshColliderEditorCommon.MakeSplitMeshTriangles(meshCache, resplitMesh);
            KssMeshColliderEditorCommon.RegistReducerTask(reducerTasks, resplitMeshCollider);
        }
    }

    static void ProcessMaterial(List<ReducerTask> reducerTasks, MeshCache meshCache, SAMeshColliderBuilder meshColliderBuilder, GameObject parentGameObject)
    {
        if (reducerTasks == null || meshCache == null || meshColliderBuilder == null || parentGameObject == null)
        {
            Debug.LogError("");
            return;
        }

        SplitMesh[] resplitMeshes = KssMeshColliderEditorCommon.MakeSplitMeshesByMaterial(meshCache);
        if (resplitMeshes == null || resplitMeshes.Length == 0)
        {
            return;
        }

        SAMeshCollider[] existingMeshColliders = KssMeshColliderEditorCommon.GetChildSAMeshColliders(parentGameObject);

        Material[] materials = meshCache.materials;

        for (int i = 0; i < resplitMeshes.Length; ++i)
        {
            SplitMesh resplitMesh = resplitMeshes[i];
            SAMeshCollider existingMeshCollider = KssMeshColliderEditorCommon.FindSAMeshCollider(existingMeshColliders, resplitMesh);
            if (existingMeshCollider != null && existingMeshCollider.modified)
            {
                continue; // Not overwrite modified SAMeshCollider.
            }

            string resplitMeshColliderName = KssMeshColliderEditorCommon.GetSAMeshColliderName_Material(materials, i);
            SAMeshCollider resplitMeshCollider = null;
            if (existingMeshCollider != null)
            {
                resplitMeshCollider = existingMeshCollider;
                KssMeshColliderEditorCommon.SetupSAMeshCollider(meshColliderBuilder, resplitMeshCollider, resplitMeshColliderName);
                resplitMesh = resplitMeshCollider.splitMesh;
            }
            else
            {
                resplitMeshCollider = KssMeshColliderEditorCommon.CreateSAMeshCollider(meshColliderBuilder, parentGameObject, resplitMeshColliderName, resplitMesh, SplitMode.Material);
            }

            if (resplitMeshCollider.splitPrimitiveEnabled)
            {
                ProcessPrimitive(reducerTasks, meshCache, meshColliderBuilder, resplitMeshCollider.gameObject, resplitMeshCollider.splitMesh);
            }
            else if (resplitMeshCollider.splitPolygonNormalEnabled)
            {
                KssMeshColliderEditorCommon.MakeSplitMeshTriangles(meshCache, resplitMesh);
                ProcessPolygon(reducerTasks, meshCache, meshColliderBuilder, resplitMeshCollider.gameObject, resplitMeshCollider.splitMesh);
            }
            else
            {
                KssMeshColliderEditorCommon.MakeSplitMeshTriangles(meshCache, resplitMesh);
                KssMeshColliderEditorCommon.RegistReducerTask(reducerTasks, resplitMeshCollider);
            }
        }
    }

    static void ProcessPrimitive(List<ReducerTask> reducerTasks, MeshCache meshCache, SAMeshColliderBuilder meshColliderBuilder, GameObject parentGameObject, SplitMesh parentSplitMesh)
    {
        if (reducerTasks == null || meshCache == null || meshColliderBuilder == null || parentGameObject == null || parentSplitMesh == null)
        {
            Debug.LogError("");
            return;
        }

        SplitMesh[] resplitMeshes = KssMeshColliderEditorCommon.MakeSplitMeshesByPrimitive(meshCache, parentSplitMesh);
        if (resplitMeshes == null || resplitMeshes.Length == 0)
        {
            return;
        }

        SAMeshCollider[] existingMeshColliders = KssMeshColliderEditorCommon.GetChildSAMeshColliders(parentGameObject);

        for (int i = 0; i < resplitMeshes.Length; ++i)
        {
            SplitMesh resplitMesh = resplitMeshes[i];
            SAMeshCollider existingMeshCollider = KssMeshColliderEditorCommon.FindSAMeshCollider(existingMeshColliders, resplitMesh);
            if (existingMeshCollider != null && existingMeshCollider.modified)
            {
                continue; // Not overwrite modified SAMeshCollider.
            }

            string resplitMeshColliderName = KssMeshColliderEditorCommon.GetSAMeshColliderName_Primitive(i);
            SAMeshCollider resplitMeshCollider = null;
            if (existingMeshCollider != null)
            {
                resplitMeshCollider = existingMeshCollider;
                KssMeshColliderEditorCommon.SetupSAMeshCollider(meshColliderBuilder, resplitMeshCollider, resplitMeshColliderName);
                resplitMesh = resplitMeshCollider.splitMesh;
            }
            else
            {
                resplitMeshCollider = KssMeshColliderEditorCommon.CreateSAMeshCollider(meshColliderBuilder, parentGameObject, resplitMeshColliderName, resplitMesh, SplitMode.Primitive);
            }

            if (resplitMeshCollider.splitPolygonNormalEnabled)
            {
                KssMeshColliderEditorCommon.MakeSplitMeshTriangles(meshCache, resplitMesh);
                ProcessPolygon(reducerTasks, meshCache, meshColliderBuilder, resplitMeshCollider.gameObject, resplitMeshCollider.splitMesh);
            }
            else
            {
                KssMeshColliderEditorCommon.MakeSplitMeshTriangles(meshCache, resplitMesh);
                KssMeshColliderEditorCommon.RegistReducerTask(reducerTasks, resplitMeshCollider);
            }
        }
    }

    static void ProcessPolygon(List<ReducerTask> reducerTasks, MeshCache meshCache, SAMeshColliderBuilder meshColliderBuilder, GameObject parentGameObject, SplitMesh parentSplitMesh)
    {
        if (reducerTasks == null || meshCache == null || meshColliderBuilder == null || parentGameObject == null || parentSplitMesh == null)
        {
            Debug.LogError("");
            return;
        }

        if (!meshColliderBuilder.splitPolygonNormalEnabled)
        {
            return;
        }

        SplitMesh[] resplitMeshes = KssMeshColliderEditorCommon.MakeSplitMeshesByPolygon(meshCache, parentSplitMesh, meshColliderBuilder.splitPolygonNormalAngle);
        if (resplitMeshes == null || resplitMeshes.Length == 0)
        {
            return;
        }

        SAMeshCollider[] existingMeshColliders = KssMeshColliderEditorCommon.GetChildSAMeshColliders(parentGameObject);

        for (int i = 0; i < resplitMeshes.Length; ++i)
        {
            SplitMesh resplitMesh = resplitMeshes[i];
            SAMeshCollider existingMeshCollider = KssMeshColliderEditorCommon.FindSAMeshCollider(existingMeshColliders, resplitMesh);
            if (existingMeshCollider != null && existingMeshCollider.modified)
            {
                continue; // Not overwrite modified SAMeshCollider.
            }

            string resplitMeshColliderName = KssMeshColliderEditorCommon.GetSAMeshColliderName_Polygon(i);
            SAMeshCollider resplitMeshCollider = null;
            if (existingMeshCollider != null)
            {
                resplitMeshCollider = existingMeshCollider;
                KssMeshColliderEditorCommon.SetupSAMeshCollider(meshColliderBuilder, resplitMeshCollider, resplitMeshColliderName); resplitMesh = resplitMeshCollider.splitMesh;
            }
            else
            {
                resplitMeshCollider = KssMeshColliderEditorCommon.CreateSAMeshCollider(meshColliderBuilder, parentGameObject, resplitMeshColliderName, resplitMesh, SplitMode.Polygon);
            }

            KssMeshColliderEditorCommon.SalvageMeshByPolygon(resplitMesh);
            KssMeshColliderEditorCommon.RegistReducerTask(reducerTasks, resplitMeshCollider);
        }
    }
}