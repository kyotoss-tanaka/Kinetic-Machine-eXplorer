#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 新しい製番を作る流れ（KMXTool の「新しい製番を作る」から Unity を起動して呼ぶ）
///   1. SolidWorks のデータをプレハブにする（Pixyz。ライセンスのある PC だけ）
///   2. Addressables を作る
///   ─ KMXTool がプロジェクトを作り、設定を Unity に出力するのを待つ ─
///   3. 実行ファイルを作る（Simuration を作成用シーンにコピーし、機械のプレハブをこの製番の物に入れ替えて作る。
///      Simuration と、Unity で開いているシーンは変えない。作成用シーンは作ったあと消す）
/// KMXTool とは作業フォルダ（-kmxWork）のファイルでやり取りする：
///   request.json（KMXTool → Unity）：取込元の種類（フォルダ／ファイル／作成済みのプレハブ）・取込元・製番
///   stage1.json （Unity → KMXTool）：製番・プレハブ・Addressables の出力
///   go.json     （KMXTool → Unity）：設定を出力したのでビルドしてよいか・実行ファイルの出力先
///   stage2.json （Unity → KMXTool）：実行ファイルのフォルダ
///   unity.log   （Unity → KMXTool）：進み具合
/// 起動：Unity.exe -projectPath &lt;KMX&gt; -executeMethod KmxNewProdPipeline.RunFromCommandLine -kmxWork &lt;作業フォルダ&gt;
/// （-batchmode は付けない：起動時の Industry Toolkit の 401 を Continue で進めるため。Pixyz も画面ありで動かす）
/// 終わったら Simuration のシーンを開いてから Unity を終える（次に開いたとき空のシーンにならないように）。
/// Unity を開いているときは KmxToolBridge 経由で RunInEditor が呼ばれ、終わっても Unity は閉じない。
/// </summary>
public static class KmxNewProdPipeline
{
    /// <summary>Pixyz の変換の出力先（PixyzProjectSettings.PrefabFolder と同じ）</summary>
    const string PrefabRoot = "Assets/3DModels";

    /// <summary>KMXTool が設定を出力するのを待つ時間（分）</summary>
    const double WaitMinutes = 60;

    class Request
    {
        /// <summary>取込元の種類：folder＝フォルダを変換、file＝ファイル（.sldasm）を変換、prefabs＝作成済みのプレハブを使う</summary>
        public string mode { get; set; } = "folder";
        /// <summary>変換するフォルダかファイル（folder・file）</summary>
        public string source { get; set; } = "";
        /// <summary>使うプレハブ（prefabs。Assets の中のファイル）</summary>
        public List<string> prefabs { get; set; } = new();
        /// <summary>製番（prefabs のとき。変換するときは Pixyz の変換が決める）</summary>
        public string prodNo { get; set; } = "";
    }

    static string workDir;
    static Request request;
    static DateTime waitStart;
    /// <summary>実行ファイルの出力先（go.json で受け取る）</summary>
    static string buildDir;
    /// <summary>この製番のプレハブ（Assets の中のファイル）</summary>
    static List<string> prodPrefabs = new();

    /// <summary>KMX のシーン（ビルドで使う）</summary>
    public const string ScenePath = "Assets/Scenes/Simuration.unity";

    /// <summary>作成用シーン（Simuration のコピー。機械のプレハブをこの製番の物にして実行ファイルを作る。作ったあと消す）</summary>
    const string BuildScenePath = "Assets/Scenes/SimurationBuild.unity";

    /// <summary>開いている Unity で行っている（終わっても Unity を閉じない）</summary>
    static bool inEditor;

    /// <summary>Unity を起動して行う（-kmxWork）</summary>
    public static void RunFromCommandLine()
    {
        inEditor = false;
        Start(PrefabLineArtRenderer.GetArg(Environment.GetCommandLineArgs(), "-kmxWork"));
    }

    /// <summary>
    /// 開いている Unity で行う（KmxToolBridge から）。開いているシーンには触らない
    /// </summary>
    public static void RunInEditor(string work)
    {
        inEditor = true;
        Start(work);
    }

    static void Start(string work)
    {
        KmxToolBridge.busy = true;
        try
        {
            workDir = work;
            if (string.IsNullOrEmpty(workDir) || !File.Exists(Path.Combine(workDir, "request.json")))
            {
                throw new Exception("作業フォルダ（-kmxWork）の request.json がありません");
            }
            request = JsonSerializer.Deserialize<Request>(File.ReadAllText(Path.Combine(workDir, "request.json"), Encoding.UTF8));
            if (request.mode == "prefabs")
            {
                Log($"開始：作成済みのプレハブ {request.prefabs.Count} 個を使います（SolidWorks からの変換は省く）");
                Register(request.prodNo, request.prefabs);
            }
            else
            {
                Log($"開始：取込元 {request.source}");
                Import();
            }
        }
        catch (Exception e)
        {
            Fail("stage1.json", e.Message);
        }
    }

    /// <summary>1. SolidWorks のデータをプレハブにする</summary>
    static void Import()
    {
#if DEV_PC
        Log("SolidWorks のデータをプレハブに変換しています（Pixyz）…");
        PrefabImporter.ImportForPipeline(request.source, (ok, prodNo) =>
        {
            if (!ok)
            {
                Fail("stage1.json", "プレハブへの変換に失敗しました（Unity のコンソールを見てください）");
                return;
            }
            Log($"変換が終わりました：製番 {prodNo}");
            Register(prodNo, null);
        });
#else
        Fail("stage1.json", "この PC の Unity では SolidWorks のデータを変換できません（Pixyz を使うスクリプトが無効：DEV_PC）。ライセンスのある PC で行うか、変換を省いてください");
#endif
    }

    /// <summary>2. Addressables を作る（prefabs が null なら変換したフォルダのプレハブ全部）</summary>
    static void Register(string prodNo, List<string> prefabs)
    {
        AssetDatabase.Refresh();
        if (prefabs == null)
        {
            string folder = Path.GetFullPath(Path.Combine(PrefabRoot, prodNo));
            prefabs = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.prefab").ToList() : new List<string>();
        }
        prefabs = prefabs.Select(Path.GetFullPath).ToList();
        prodPrefabs = prefabs;
        string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
        var outside = prefabs.Where(d => !d.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !File.Exists(d)).ToList();
        if (prefabs.Count == 0 || outside.Count > 0)
        {
            Fail("stage1.json", prefabs.Count == 0 ? "プレハブがありません"
                : "Unity プロジェクトの Assets の中に無いプレハブがあります: " + string.Join(", ", outside));
            return;
        }
        if (string.IsNullOrEmpty(prodNo))
        {
            Fail("stage1.json", "製番がありません");
            return;
        }
        var folders = prefabs.Select(Path.GetDirectoryName).Distinct().ToList();
        Log($"Addressables を作っています…（プレハブ {prefabs.Count} 個）");
        AddressablePrefabRegistrar.RegisterForPipeline(folders, prefabs, prodNo, error =>
        {
            if (error != null)
            {
                Fail("stage1.json", "Addressables を作れませんでした: " + error);
                return;
            }
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            string aaDir = Path.Combine(projectPath, "Library", "com.unity.addressables", "aa", prodNo);
            WriteStage("stage1.json", new Dictionary<string, object>
            {
                ["ok"] = true,
                ["prodNo"] = prodNo,
                ["aaDir"] = aaDir,
                ["prefabs"] = prefabs.Select(Path.GetFileNameWithoutExtension).OrderBy(d => d).ToList(),
            });
            Log("Addressables ができました。KMXTool が設定を出力するのを待っています…");
            waitStart = DateTime.Now;
            EditorApplication.update += WaitForGo;
        });
    }

    /// <summary>KMXTool が go.json を置くのを待つ</summary>
    static void WaitForGo()
    {
        string go = Path.Combine(workDir, "go.json");
        if (!File.Exists(go))
        {
            if ((DateTime.Now - waitStart).TotalMinutes > WaitMinutes)
            {
                EditorApplication.update -= WaitForGo;
                Fail("stage2.json", "KMXTool からの合図が来ないので止めました");
            }
            return;
        }
        EditorApplication.update -= WaitForGo;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(go, Encoding.UTF8));
            if (!doc.RootElement.GetProperty("ok").GetBoolean())
            {
                Log("KMXTool で止めたので、ビルドせずに終わります");
                Finish(1);
                return;
            }
            buildDir = doc.RootElement.GetProperty("buildDir").GetString();
        }
        catch (Exception e)
        {
            Fail("stage2.json", "go.json を読めません: " + e.Message);
            return;
        }
        // 次の更新でビルドする（合図を読んだ処理の中で重い処理をしない）
        EditorApplication.delayCall += Build;
    }

    /// <summary>3. 実行ファイルを作る</summary>
    static void Build()
    {
        try
        {
            AssetDatabase.Refresh();
            CreateBuildScene();
            Log("実行ファイルを作っています…");
            string exeDir = BuildAndRun.BuildForPipeline(buildDir, BuildScenePath);
            DeleteBuildScene();
            WriteStage("stage2.json", new Dictionary<string, object> { ["ok"] = true, ["exeDir"] = Path.GetFullPath(exeDir) });
            Log("実行ファイルができました");
            Finish(0);
        }
        catch (Exception e)
        {
            DeleteBuildScene();
            Fail("stage2.json", e.Message);
        }
    }

    /// <summary>
    /// 作成用シーンを作る：Simuration をコピーし、機械のプレハブ（一番上にある、Assets/3DModels から来た物と、
    /// 元のプレハブが無くなった物）を消して、この製番のプレハブを入れる。部屋のモデル等、ほかの物は残す。
    /// 実行時はシーンにある同じ名前のプレハブを使う（ParameterLoader.LoadPrefabModel）。
    /// 毎回 Simuration からコピーするので、Simuration に足した物も実行ファイルに入る。
    /// 開いているシーンを閉じないよう、追加で開いて、保存したら閉じる
    /// </summary>
    static void CreateBuildScene()
    {
        AssetDatabase.DeleteAsset(BuildScenePath);
        if (!AssetDatabase.CopyAsset(ScenePath, BuildScenePath))
        {
            throw new Exception("作成用シーンを作れません: " + BuildScenePath);
        }
        var scene = EditorSceneManager.OpenScene(BuildScenePath, OpenSceneMode.Additive);
        try
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                bool missing = PrefabUtility.IsPrefabAssetMissing(go);
                string source = PrefabUtility.IsAnyPrefabInstanceRoot(go) ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) : "";
                if (missing || source.StartsWith(PrefabRoot, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"作成用シーンから消します：{go.name}{(missing ? "（元のプレハブが無い）" : "")}");
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            string assets = Path.GetFullPath(Application.dataPath);
            foreach (var path in prodPrefabs)
            {
                string assetPath = "Assets" + path.Substring(assets.Length).Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (asset == null)
                {
                    throw new Exception("プレハブを読めません: " + assetPath);
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                // 実行時は PrefabInfo の名前（ファイル名）と同じ名前の物を使う
                instance.name = asset.name;
                Log($"作成用シーンに入れます：{asset.name}");
            }
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new Exception("作成用シーンを保存できません: " + BuildScenePath);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// 作成用シーンを消す（作ったあと残さない）
    /// </summary>
    static void DeleteBuildScene()
    {
        try
        {
            AssetDatabase.DeleteAsset(BuildScenePath);
        }
        catch (Exception e)
        {
            Log($"作成用シーンを消せませんでした（{BuildScenePath}）: {e.Message}");
        }
    }

    static void Fail(string stage, string message)
    {
        Debug.LogError("[NewProd] " + message);
        Log("失敗：" + message);
        if (workDir != null)
        {
            WriteStage(stage, new Dictionary<string, object> { ["ok"] = false, ["error"] = message });
        }
        Finish(1);
    }

    /// <summary>
    /// 終わる。Unity を起動して行ったときは Simuration のシーンを開いてから Unity を終える
    /// （次に開いたときも Simuration になる）。開いている Unity で行ったときは閉じない
    /// </summary>
    static void Finish(int code)
    {
        EditorApplication.update -= WaitForGo;
        KmxToolBridge.busy = false;
        if (inEditor)
        {
            inEditor = false;
            return;
        }
        try
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NewProd] Simuration のシーンを開けませんでした: " + e.Message);
        }
        EditorApplication.Exit(code);
    }

    /// <summary>途中のファイルを読まれないよう、別名で書いてから置き換える</summary>
    static void WriteStage(string name, Dictionary<string, object> data)
    {
        string path = Path.Combine(workDir, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(path + ".tmp", path);
    }

    static void Log(string message)
    {
        Debug.Log("[NewProd] " + message);
        if (workDir != null)
        {
            File.AppendAllText(Path.Combine(workDir, "unity.log"), $"{DateTime.Now:HH:mm:ss} {message}\n", new UTF8Encoding(false));
        }
    }
}
#endif
