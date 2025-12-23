#if UNITY_EDITOR
using Meta.WitAi.CallbackHandlers;
using Meta.XR.Acoustics;
using Parameters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.PixyzPlugin4Unity.Actions;
using UnityEngine;
//using UnityEditor.PixyzPlugin4Unity;
//using UnityEditor.PixyzPlugin4Unity.Actions;

public class PrefabMeshMerger
{
    /// <summary>
    /// マテリアルキー
    /// </summary>
    class MaterialKey
    {
        public Shader shader;
        public Color baseColor;
        public Texture baseMap;
        public float metallic;
        public float smoothness;

        public override bool Equals(object obj)
        {
            if (obj is not MaterialKey o) return false;
            return shader == o.shader &&
                   baseColor.Equals(o.baseColor) &&
                   baseMap == o.baseMap &&
                   Mathf.Approximately(metallic, o.metallic) &&
                   Mathf.Approximately(smoothness, o.smoothness);
        }

        public override int GetHashCode()
        {
            int hash = shader.GetHashCode();
            hash = hash * 31 + baseColor.GetHashCode();
            hash = hash * 31 + (baseMap ? baseMap.GetHashCode() : 0);
            hash = hash * 31 + metallic.GetHashCode();
            hash = hash * 31 + smoothness.GetHashCode();
            return hash;
        }
    }

    private static bool isProcessing = false;
    private static List<GameObject> hiddenObjs = new List<GameObject>();

    [MenuItem("Kyotoss/Merge Prefab Meshes(VR)", false , 3)]
    public static void MergePrefabMeshes()
    {
        isProcessing = true;
        // ダイアログを開いて、OK時にコールバックで処理を行う
        string path = EditorUtility.OpenFolderPanel("VR用にマージする機番フォルダを選択", Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels")), "");
        if (path != "")
        {
            path = Path.Combine(path, "VR");
            if (Directory.Exists(path))
            {
                MergePrefabProcess(path);
            }
            else
            {
                EditorUtility.DisplayDialog("エラー", "先にVR用Prefabを作成してください", "OK");
            }
        }
        isProcessing = false;
    }

    // validate関数（trueなら有効、falseなら無効）
    [MenuItem("Kyotoss/Merge Prefab Meshes(VR)", true)]
    private static bool ValidateMergePrefabMeshes()
    {
        return !isProcessing; // 処理中なら無効化
    }

    /// <summary>
    /// マージ処理
    /// </summary>
    /// <param name="folder"></param>
    public static async void MergePrefabProcess(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (Directory.Exists(folder))
        {
            // プレハブファイル取得
            var files = Directory.GetFiles(folder).ToList().FindAll(d => (Path.GetExtension(d) == ".prefab") && !Path.GetFileName(d).Contains("_VR.prefab"));
            if (files.Count > 0)
            {
                var dirPath = folder;//Path.Combine(folder, "VR");
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
                var mergedPath = Path.Combine(dirPath, "Merged");
                if (Directory.Exists(mergedPath))
                {
                    Directory.Delete(mergedPath, true);
                }
                Directory.CreateDirectory(mergedPath);
                mergedPath = mergedPath.Replace("\\", "/").Replace(Application.dataPath, "Assets");
                var unitSettings = (List<UnitSetting>)await GlobalScript.LoadListJson<List<UnitSetting>>("UnitInfo");
                var hiddenSettings = (List<HiddenUnit>)await GlobalScript.LoadListJson<List<HiddenUnit>>("HiddenUnitInfo");
                var excludePaths = new HashSet<string>();
                foreach (var unit in unitSettings)
                {
                    if (unit.path != "")
                    {
                        excludePaths.Add(unit.path);
                    }
                }
                EditorApplication.delayCall += () =>
                {
                    foreach (var file in files)
                    {
                        EditorUtility.DisplayProgressBar($"Prefabマージ{files.IndexOf(file) + 1}/{files.Count}", $"Prefabロード中...", (float)files.IndexOf(file) / (float)files.Count);
                        // 保存先
                        var filePath = Path.Combine(Path.Combine(dirPath, "Merged"), Path.GetFileNameWithoutExtension(file) + "_VR" + ".prefab");
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        // Prefab を編集用にロード
                        GameObject root = PrefabUtility.LoadPrefabContents(file);
                        root.name = Path.GetFileNameWithoutExtension(file);
                        var hiddenPaths = RenewHiddenObjs(root, hiddenSettings);
                        try
                        {
                            // マージ処理
                            var paths = new HashSet<string>();
                            /*
                            paths.AddRange(excludePaths);
                            paths.AddRange(hiddenPaths);
                            */
                            MergeSameMaterials(root);
                            UnifySimilarColorMaterials(root, 0.05f);
                            MergeWholePrefabExcludeMovable(root.transform, paths, mergedPath, $"Prefabマージ{files.IndexOf(file) + 1}/{files.Count}");
                            // Prefab に保存
                            PrefabUtility.SaveAsPrefabAsset(root, filePath);
                        }
                        catch (Exception ex)
                        {
                            EditorUtility.DisplayDialog("エラー", ex.Message, "OK");
                        }
                        finally
                        {
                            PrefabUtility.UnloadPrefabContents(root);
                            EditorUtility.ClearProgressBar();
                        }
                    }
                    isProcessing = false;
                };
            }
            else
            {
                isProcessing = false;
            }
        }
        else
        {
            isProcessing = false;
        }
    }
    static void MergeWholePrefabExcludeMovable(Transform root, HashSet<string> movablePaths, string mergedPath, string label)
    {
        var meshPath_s = mergedPath + "/Meshes";
        var materialPath_s = mergedPath + "/Materials";
        if (!Directory.Exists(meshPath_s))
        {
            Directory.CreateDirectory(meshPath_s);
            Directory.CreateDirectory(materialPath_s);
        }

        // ===== 対象 MeshFilter 抽出 =====
        var targets = root.GetComponentsInChildren<MeshFilter>()
            .Select(mf => new
            {
                mf,
                mr = mf.GetComponent<MeshRenderer>()
            })
            .Where(x =>
            {
                if (x.mf.sharedMesh == null) return false;
                if (!IsMergeSafe(x.mf)) return false;
                if (x.mf.transform == root) return false;
                if (x.mr == null || x.mr.sharedMaterials == null) return false;

                string path = GetHierarchyPath(x.mf.transform, root);
                return !movablePaths.Any(mp =>
                    path == mp || path.StartsWith(mp + "/"));
            })
            .ToList();

        if (targets.Count == 0)
            return;

        // ===== (Material × subMeshIndex) で分解 =====
        var entries = new List<(Material mat, MeshFilter mf, int subMesh)>();

        foreach (var t in targets)
        {
            Mesh mesh = t.mf.sharedMesh;
            var mats = t.mr.sharedMaterials;

            int count = Mathf.Min(mesh.subMeshCount, mats.Length);
            for (int i = 0; i < count; i++)
            {
                if (mats[i] == null)
                    continue;

                entries.Add((mats[i], t.mf, i));
            }
        }

        // ===== Material ごとにマージ =====
        int index = 0;
        var groups = entries.GroupBy(e => e.mat).ToList();
        foreach (var group in groups)
        {
            EditorUtility.DisplayProgressBar(label, $"MaterialとMesh作成中...{groups.IndexOf(group) + 1}/{groups.Count}", (float)(groups.IndexOf(group) + 1) / (float)groups.Count);
            Material safeMat = GetOrCreateMaterialAsset(group.Key, materialPath_s);
            if (safeMat == null)
                continue;

            var combines = new List<CombineInstance>();

            foreach (var e in group)
            {
                combines.Add(new CombineInstance
                {
                    mesh = e.mf.sharedMesh,
                    subMeshIndex = e.subMesh,
                    transform =
                        root.worldToLocalMatrix *
                        e.mf.transform.localToWorldMatrix
                });
            }

            if (combines.Count == 0)
                continue;

            Mesh merged = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                name = $"{root.name}_{index++}"
            };

            merged.CombineMeshes(combines.ToArray(), true, true, false);

            string meshAssetPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{meshPath_s}/{merged.name}.asset");

            AssetDatabase.CreateAsset(merged, meshAssetPath);

            var mergedObj = new GameObject(merged.name);
            mergedObj.transform.SetParent(root, false);

            mergedObj.AddComponent<MeshFilter>().sharedMesh = merged;
            mergedObj.AddComponent<MeshRenderer>().sharedMaterial = safeMat;
        }

        // ===== 元オブジェクト削除 =====
        foreach (var t in targets)
        {
            UnityEngine.Object.DestroyImmediate(t.mf.gameObject);
        }
        // 不要部削除
        var tmp = root.GetComponentsInChildren<Transform>().Where(d => d.name.Contains(".sldasm")).FirstOrDefault();
        if (tmp != null)
        {
            UnityEngine.Object.DestroyImmediate(tmp.gameObject);
        }

        AssetDatabase.SaveAssets();
    }

    static bool IsMergeSafe(MeshFilter mf)
    {
        if (mf.sharedMesh == null)
            return false;

        if (mf.GetComponent<LineRenderer>() != null)
            return false;

        var mr = mf.GetComponent<MeshRenderer>();
        if (mr == null || mr.sharedMaterial == null)
            return false;

        if (!IsTriangleMesh(mf.sharedMesh))
            return false;

        return true;
    }

    static bool IsTriangleMesh(Mesh mesh)
    {
        if (mesh == null) return false;

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (mesh.GetTopology(i) != MeshTopology.Triangles)
                return false;
        }
        return true;
    }

    static string GetHierarchyPath(Transform t, Transform root)
    {
        var stack = new Stack<string>();
        while (t != null && t != root)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return root.name + "/" + string.Join("/", stack);
    }

    static Material GetOrCreateMaterialAsset(Material src, string materialFolder)
    {
        if (!Directory.Exists(materialFolder))
        {
            Directory.CreateDirectory(materialFolder);
        }
        if (src == null)
            return null;

        // すでに Asset ならそのまま使う
        string srcPath = AssetDatabase.GetAssetPath(src);
        if (!string.IsNullOrEmpty(srcPath))
        {
            return AssetDatabase.LoadAssetAtPath<Material>(srcPath);
        }

        // フォルダ作成
        if (!AssetDatabase.IsValidFolder(materialFolder))
        {
            string parent = Path.GetDirectoryName(materialFolder).Replace("\\", "/");
            string name = Path.GetFileName(materialFolder);
            AssetDatabase.CreateFolder(parent, name);
        }

        // Material 複製
        Material copy = new Material(src);
        copy.name = src.name;

        string matPath = $"{materialFolder}/{copy.name}.mat";
        matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);

        AssetDatabase.CreateAsset(copy, matPath);
        AssetDatabase.SaveAssets();

        return copy;
    }

    static void SaveMovableAssets(Transform root, HashSet<string> movablePaths, string meshSaveDir, string materialSaveDir)
    {
        var renderers = GetMovableRenderers(root, movablePaths);

        foreach (var mr in renderers)
        {
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;

            // ===== Mesh =====
            Mesh mesh = mf.sharedMesh;

            if (!AssetDatabase.Contains(mesh))
            {
                string meshPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{meshSaveDir}/{mesh.name}.asset");

                Mesh meshCopy = UnityEngine.Object.Instantiate(mesh);
                AssetDatabase.CreateAsset(meshCopy, meshPath);

                // ★ 差し替え（必須）
                mf.sharedMesh = meshCopy;
                EditorUtility.SetDirty(mf);
            }

            // ===== Material =====
            var mats = mr.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                    continue;

                if (!AssetDatabase.Contains(mats[i]))
                {
                    string matPath = AssetDatabase.GenerateUniqueAssetPath(
                        $"{materialSaveDir}/{mats[i].name}.mat");

                    Material matCopy = UnityEngine.Object.Instantiate(mats[i]);
                    AssetDatabase.CreateAsset(matCopy, matPath);

                    // ★ 差し替え（必須）
                    mats[i] = matCopy;
                    changed = true;
                }
            }

            if (changed)
            {
                mr.sharedMaterials = mats;
                EditorUtility.SetDirty(mr);
            }
        }

        AssetDatabase.SaveAssets();
    }

    static List<MeshRenderer> GetMovableRenderers(Transform root, HashSet<string> movablePaths)
    {
        return root.GetComponentsInChildren<MeshRenderer>()
            .Where(mr =>
            {
                string path = GetHierarchyPath(mr.transform, root);
                return movablePaths.Any(mp =>
                    path == mp || path.StartsWith(mp + "/"));
            })
            .ToList();
    }

    /// <summary>
    /// 無視オブジェクト更新
    /// </summary>
    private static HashSet<string> RenewHiddenObjs(GameObject root, List<HiddenUnit> hiddenSettings)
    {
        var hidden = new HashSet<string>();
        void GetPath(HiddenUnit m, List<string> path)
        {
            if ((m.parent == null) || (m.parent == "") || path.Contains(m.parent))
            {
                path.Reverse();
                hidden.Add(string.Join('/', path));
            }
        }
        foreach (var m in hiddenSettings)
        {
            if (m.isEnable)
            {
                if (m.mode == 0)
                {
                    // 一致
                    foreach (var o in root.transform.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name == m.name))
                    {
                        GetPath(m, CommonFunction.GetScenePath(o.gameObject));
                    }
                }
                else if (m.mode == 1)
                {
                    // 前方一致
                    foreach (var o in root.transform.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.StartsWith(m.name)))
                    {
                        GetPath(m, CommonFunction.GetScenePath(o.gameObject));
                    }
                }
                else if (m.mode == 2)
                {
                    // 後方一致
                    foreach (var o in root.transform.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.EndsWith(m.name)))
                    {
                        GetPath(m, CommonFunction.GetScenePath(o.gameObject));
                    }
                }
                else if (m.mode == 3)
                {
                    // 含まれている
                    foreach (var o in root.transform.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains(m.name)))
                    {
                        GetPath(m, CommonFunction.GetScenePath(o.gameObject));
                    }
                }
            }
        }
        return hidden;
    }
    #region マテリアル統合
    static void MergeSameMaterials(GameObject root)
    {
        var map = new Dictionary<MaterialKey, Material>();

        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = renderer.sharedMaterials;

            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;

                var key = new MaterialKey
                {
                    shader = mat.shader,
                    baseColor = mat.HasProperty("_BaseColor")
                        ? mat.GetColor("_BaseColor")
                        : Color.white,
                    baseMap = mat.HasProperty("_BaseMap")
                        ? mat.GetTexture("_BaseMap")
                        : null,
                    metallic = mat.HasProperty("_Metallic")
                        ? mat.GetFloat("_Metallic")
                        : 0f,
                    smoothness = mat.HasProperty("_Smoothness")
                        ? mat.GetFloat("_Smoothness")
                        : 0f,
                };

                if (!map.TryGetValue(key, out var unified))
                {
                    unified = mat;
                    map.Add(key, unified);
                }

                mats[i] = unified;
            }
            renderer.sharedMaterials = mats;
        }
    }
    static void UnifySimilarColorMaterials(GameObject root, float threshold)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);

        // 不透明 / 透明 で完全分離
        var opaqueGroups = new List<(Color color, Material mat)>();
        var transparentGroups = new List<(Color color, Material mat)>();

        foreach (var renderer in renderers)
        {
            var mats = renderer.sharedMaterials;
            var newMats = new Material[mats.Length];

            for (int i = 0; i < mats.Length; i++)
            {
                var srcMat = mats[i];
                if (srcMat == null)
                {
                    newMats[i] = null;
                    continue;
                }

                bool isTransparent = IsTransparent(srcMat);

                Color srcColor = Color.gray;
                if (srcMat.HasProperty("_BaseColor"))
                    srcColor = srcMat.GetColor("_BaseColor");

                var groups = isTransparent ? transparentGroups : opaqueGroups;

                // 近い色を探す
                Material targetMat = null;
                foreach (var group in groups)
                {
                    if (ColorDistance(group.color, srcColor) < threshold)
                    {
                        targetMat = group.mat;
                        break;
                    }
                }

                // なければ新規作成
                if (targetMat == null)
                {
                    targetMat = CreateUnlitMaterial(srcColor, isTransparent);
                    groups.Add((srcColor, targetMat));
                }

                newMats[i] = targetMat;
            }

            renderer.sharedMaterials = newMats;
        }
    }

    static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }

    static Material CreateUnlitMaterial(Color color, bool transparent)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = transparent
            ? $"Unified_Transparent_{UnityEngine.ColorUtility.ToHtmlStringRGB(color)}"
            : $"Unified_Opaque_{UnityEngine.ColorUtility.ToHtmlStringRGB(color)}";

        mat.SetColor("_BaseColor", color);

        if (transparent)
        {
            mat.SetFloat("_Surface", 1); // Transparent
            mat.renderQueue = 3000;
        }
        else
        {
            mat.SetFloat("_Surface", 0); // Opaque
            mat.renderQueue = 2000;
        }
        return mat;
    }

    static bool IsTransparent(Material mat)
    {
        if (mat == null) return false;

        // URP Lit / Unlit 共通
        if (mat.HasProperty("_Surface"))
        {
            // 0 = Opaque, 1 = Transparent
            return mat.GetFloat("_Surface") > 0.5f;
        }

        // フォールバック
        return mat.renderQueue >= 3000;
    }

    #endregion マテリアル統合

}
#endif