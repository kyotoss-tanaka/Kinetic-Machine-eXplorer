#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using Unity.VisualScripting;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine.XR.OpenXR;
using Meta.XR;
using UnityEngine.XR.Management;

public class BuildAndRun
{
    private static string serverPath = "ServerData";

    private static string bundlePath = "StandaloneWindows64";

    class BuildConfig
    {
        public string target;
        public string outputPath;
        public List<string> scenes;
        public BuildOptions buildOptions;
    }

    private static GameObject normalCamera;
    private static GameObject vrCamera;
    private static GameObject mrCamera;

    static string scenePath = "Assets/Scenes/Simuration.unity";

    [MenuItem("Kyotoss/Master Build and Run", false, 51)]
    public static void ReleaseBuildAndRunMaster()
    {
        try
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("シーンを保存しました。");
            }

            string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
            if (!File.Exists(configPath))
            {
                Debug.LogError("設定ファイルが見つかりません: " + configPath);
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);
            build.isMaster = true;
            build.name = "Master";

            build.isRelease = true;
            var folderPath = BuildAndRunProcess(build, true);

            /*
            build.isRelease = false;
            BuildAndRunProcess(build, false);
            */
            if (folderPath != "")
            {
                // エクスプローラーで開く
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                // タイトル、メッセージ、ボタン名
                EditorUtility.DisplayDialog("情報", "ビルドが完了しました。", "OK");
            }
        }
        catch
        {
        }
    }

    [MenuItem("Kyotoss/WebGL Build and Run (Master)", false, 55)]
    public static void ReleaseBuildAndRunMasterWebGL()
    {
        try
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("シーンを保存しました。");
            }

            string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
            if (!File.Exists(configPath))
            {
                Debug.LogError("設定ファイルが見つかりません: " + configPath);
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);
            build.isMaster = true;
            build.name = "Master";
            build.isVR = false;
            build.isMR = false;

            build.isRelease = true;
            var folderPath = BuildAndRunProcess(build, true, false, true);   // isWeb = true

            if (folderPath != "")
            {
                // エクスプローラーで開く
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                EditorUtility.DisplayDialog("情報", "WebGLビルドが完了しました。", "OK");
            }
        }
        catch (Exception e)
        {
            // WebGLは詰まりやすいので失敗内容をログに出す
            Debug.LogError($"WebGLビルド失敗: {e}");
        }
    }
    /*
    [MenuItem("Kyotoss/Master Build and Run(Debug)", false, 52)]
    public static void DebugBuildAndRunMaster()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("シーンを保存しました。");
        }

        string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
        if (!File.Exists(configPath))
        {
            Debug.LogError("設定ファイルが見つかりません: " + configPath);
            return;
        }

        string json = File.ReadAllText(configPath, Encoding.UTF8);
        Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);
        build.isRelease = false;
        build.isMaster = true;
        build.name = "Master";

        BuildAndRunProcess(build);
    }
    */

    [MenuItem("Kyotoss/Build and Run from KMXTool Config", false, 53)]
    public static void ReleaseBuildAndRunFromConfig()
    {
        try
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("シーンを保存しました。");
            }

            string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
            if (!File.Exists(configPath))
            {
                Debug.LogError("設定ファイルが見つかりません: " + configPath);
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);

            build.isRelease = true;
            var folderPath = BuildAndRunProcess(build, true, true);
            /*
            build.isRelease = false;
            BuildAndRunProcess(build, false, true);
            */
            if (folderPath != "")
            {
                // エクスプローラーで開く
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                // タイトル、メッセージ、ボタン名
                EditorUtility.DisplayDialog("情報", "ビルドが完了しました。", "OK");
            }
        }
        catch
        {
        }
    }

    [MenuItem("Kyotoss/WebGL Build and Run from KMXTool Config", false, 56)]
    public static void ReleaseBuildAndRunFromConfigWebGL()
    {
        try
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("シーンを保存しました。");
            }

            string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
            if (!File.Exists(configPath))
            {
                Debug.LogError("設定ファイルが見つかりません: " + configPath);
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);
            build.isVR = false;
            build.isMR = false;

            build.isRelease = true;
            var folderPath = BuildAndRunProcess(build, true, true, true);   // isProd = true, isWeb = true
            if (folderPath != "")
            {
                // エクスプローラーで開く
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                EditorUtility.DisplayDialog("情報", "WebGLビルドが完了しました。", "OK");
            }
        }
        catch (Exception e)
        {
            // WebGLは詰まりやすいので失敗内容をログに出す
            Debug.LogError($"WebGLビルド失敗: {e}");
        }
    }

    [MenuItem("Kyotoss/Build and Run from KMXTool Config(Debug)", false, 54)]
    public static void DebugAndRunFromConfig()
    {
        try
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("シーンを保存しました。");
            }

            string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
            if (!File.Exists(configPath))
            {
                Debug.LogError("設定ファイルが見つかりません: " + configPath);
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);

            build.isRelease = false;
            var folderPath = BuildAndRunProcess(build, false, true);
            if (folderPath != "")
            {
                // エクスプローラーで開く
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                // タイトル、メッセージ、ボタン名
                EditorUtility.DisplayDialog("情報", "ビルドが完了しました。", "OK");
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// ビルド処理
    /// </summary>
    private static string BuildAndRunProcess(Parameters.BuildConfig build, bool isRun, bool isProd = false, bool isWeb = false)
    {
        var isAndroid = build.isVR || build.isMR;
        var platformDir = isWeb ? "Web" : (isAndroid ? "Android" : "Windows");
        var productName = build.isMaster ? "KMXMaster" : (build.isMR ? $"{build.mechId}_{build.name}(MR)" : build.isVR ? $"{build.mechId}_{build.name}(VR)" : $"{build.mechId}_{build.name}");
        productName = productName.Replace("/", " ");
        var productDir = Path.Combine(Path.Combine(Path.Combine("Builds", platformDir), build.isRelease ? "Release" : "Debug"), productName);

        BuildConfig config = new BuildConfig
        {
            target = isWeb ? "webgl" : (isAndroid ? "Android" : "Windows"),
            // WebGLは単一ファイルではなくフォルダ出力（index.html 一式）
            outputPath = isWeb ? productDir : (isAndroid ? $"{productDir}.apk" : $"{productDir}/KMX.exe"),
            scenes = new List<string> { scenePath },
            buildOptions = build.isRelease ? BuildOptions.None : BuildOptions.Development | BuildOptions.ConnectWithProfiler | BuildOptions.AllowDebugging// | BuildOptions.EnableDeepProfilingSupport
        };

        // シーン読み込み
        //        SwitchBuild(build);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ビルドターゲットの変換
        if (!TryParseTarget(config.target, out BuildTarget target, out BuildTargetGroup group))
        {
            Debug.LogError("不正なビルドターゲット: " + config.target);
            return "";
        }

        // プラットフォーム切り替え
        EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

        // PlayerSettings
        PlayerSettings.companyName = "Kyoto Seisakusho Co., Ltd.";
        PlayerSettings.applicationIdentifier = $"com.kyotoss.kmx_{build.mechId.ToShortString() + (build.isMR ? "_mr" : (build.isVR ? "_vr" : ""))}";
        PlayerSettings.productName = $"KMX {productName}";
        PlayerSettings.bundleVersion = "0.1";

        PlayerSettings.Android.bundleVersionCode = 1;

        // 新形式 API
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = config.scenes.ToArray(),
            locationPathName = config.outputPath,
            target = target,
            // WebGL は LZ4HC 圧縮オプション非対応のため除外
            options = (isRun ? BuildOptions.AutoRunPlayer : BuildOptions.None) | config.buildOptions | (isWeb ? BuildOptions.None : BuildOptions.CompressWithLz4HC)
        };

        if (isWeb)
        {
            // WebGL: ローカル/ヘッダ未設定の環境でも読み込めるよう Gzip + 解凍フォールバック
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
        }

        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        //        EditorUserBuildSettings.development = !build.isRelease;

        // Addressable設定
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 1;
        AddPrefabToAddressables("Assets/Resources/Prefabs/DummyPrefab.prefab");
        AddressableAssetGroupSchema sc = settings.DefaultGroup.Schemas.Find(d => d.GetType() == typeof(BundledAssetGroupSchema));
        var buildName = ((BundledAssetGroupSchema)sc).BuildPath.GetName(settings);
        var loadName = ((BundledAssetGroupSchema)sc).LoadPath.GetName(settings);
        // WebGL はサーバ配信不可のため、ローカル同梱（StreamingAssets）パスにする
        var savePath = isWeb ? "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]" : $"{serverPath}/{bundlePath}";
        var loadPath = isWeb ? "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]" : $"{serverPath}/{bundlePath}";
        settings.profileSettings.SetValue(settings.activeProfileId, buildName, savePath);
        settings.profileSettings.SetValue(settings.activeProfileId, loadName, loadPath);

        QualitySettings.SetQualityLevel(System.Array.IndexOf(QualitySettings.names, "High"), true);

        // パス設定
        string projectPath = Directory.GetParent(Application.dataPath).FullName;
        string folderPath = Path.Combine(projectPath, productDir);
        var jsonPath = Path.Combine(folderPath, "RuntimeActionBindings.json");
        if (File.Exists(jsonPath))
        {
            File.Delete(jsonPath);
        }

        // 保存
        AssetDatabase.SaveAssets();

        // プログラム実行
        BuildPipeline.BuildPlayer(options);

        // Addressable設定戻す
        settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 0;
        // 保存
        AssetDatabase.SaveAssets();

        if (!isProd)
        {
            // 製番情報のデータを削除
            var dataPath = Path.Combine(folderPath, "KMX_Data/StreamingAssets/Datas");
            var prefabPath = Path.Combine(folderPath, "KMX_Data/ServerData");
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, true);
            }
            if (Directory.Exists(prefabPath))
            {
                Directory.Delete(prefabPath, true);
            }
            // フォルダだけ作成しておく
            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(prefabPath);
        }
        return folderPath;
    }


    [MenuItem("Kyotoss/Switch to Normal Config", false, 101)]
    public static void SwitchToWindowsConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { });
    }

    [MenuItem("Kyotoss/Switch to VR Config", false, 102)]
    public static void SwitchToVRConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { isVR = true });
    }

    [MenuItem("Kyotoss/Switch to MR Config", false, 103)]
    public static void SwitchToMRConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { isMR = true });
    }

    static void SwitchBuild(Parameters.BuildConfig build)
    {
        // シーン読み込み
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        
        // イベントシステム取得
        var eventSystem = FindInScene(scene, "EventSystem");
        var pointableInput = eventSystem != null ? eventSystem.GetComponent<UnityEngine.EventSystems.PointerInputModule>() : null;
        if (pointableInput != null)
        {
            pointableInput.enabled = build.isVR || build.isMR;
        }

        // オブジェクトを検索
        var normalSetting = FindInScene(scene, "NormalSetting");
        var vrSetting = FindInScene(scene, "VRSetting");
        var mrSetting = FindInScene(scene, "MRSetting");
        var parent = normalSetting != null ? normalSetting.transform.parent : (vrSetting != null ? vrSetting.transform.parent : mrSetting.transform.parent);
        
        // 設定切り替え
        if (build.isVR)
        {
            if (vrCamera == null)
            {
                vrCamera = GlobalScript.LoadPrefabObject("Prefabs/Camera", "VRSetting", false)[0];
            }
            if (vrSetting == null)
            {
                vrSetting = GameObject.Instantiate(vrCamera) as GameObject;
                vrSetting.name = "VRSetting";
                vrSetting.transform.parent = parent;
            }
            if (normalSetting != null)
            {
                GameObject.DestroyImmediate(normalSetting);
            }
            if (mrSetting != null)
            {
                GameObject.DestroyImmediate(mrSetting);
            }
        }
        else if (build.isMR)
        {
            if (mrCamera == null)
            {
                mrCamera = GlobalScript.LoadPrefabObject("Prefabs/Camera", "MRSetting", false)[0];
            }
            if (mrSetting == null)
            {
                mrSetting = GameObject.Instantiate(mrCamera) as GameObject;
                mrSetting.name = "MRSetting";
                mrSetting.transform.parent = parent;
            }
            if (normalSetting != null)
            {
                GameObject.DestroyImmediate(normalSetting);
            }
            if (vrSetting != null)
            {
                GameObject.DestroyImmediate(vrSetting);
            }
        }
        else
        {
            if (normalCamera == null)
            {
                normalCamera = GlobalScript.LoadPrefabObject("Prefabs/Camera", "NormalSetting", false)[0];
            }
            if (normalSetting == null)
            {
                normalSetting = GameObject.Instantiate(normalCamera) as GameObject;
                normalSetting.name = "NormalSetting";
                normalSetting.transform.parent = parent;
            }
            if (vrSetting != null)
            {
                GameObject.DestroyImmediate(vrSetting);
            }
            if (mrSetting != null)
            {
                GameObject.DestroyImmediate(mrSetting);
            }
        }
        // Meta XRの自動ロード機能設定
        var settings = XRGeneralSettings.Instance;
        settings.InitManagerOnStart = build.isXR;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        // シーン保存
        EditorSceneManager.SaveScene(scene);
    }

    static bool TryParseTarget(string name, out BuildTarget target, out BuildTargetGroup group)
    {
        target = BuildTarget.NoTarget;
        group = BuildTargetGroup.Unknown;

        switch (name.ToLower())
        {
            case "android":
                target = BuildTarget.Android;
                group = BuildTargetGroup.Android;
                return true;
            case "windows":
                target = BuildTarget.StandaloneWindows64;
                group = BuildTargetGroup.Standalone;
                return true;
            case "webgl":
                target = BuildTarget.WebGL;
                group = BuildTargetGroup.WebGL;
                return true;
            // 他のターゲットも必要に応じて追加
            default:
                return false;
        }
    }

    static BuildOptions ParseBuildOptions(string opt)
    {
        if (Enum.TryParse(opt, out BuildOptions parsed))
            return parsed;
        return BuildOptions.None;
    }

    // 非アクティブでも検索できる再帰関数
    private static GameObject FindInScene(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = FindInChildren(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform FindInChildren(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            var result = FindInChildren(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private static void PushFileWithADB(string localFilePath, string devicePath)
    {
        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
        psi.FileName = @"C:\Program Files\Unity\Hub\Editor\6000.0.50f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe";
        psi.Arguments = $"install -r \"{localFilePath}\"";
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        try
        {
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                UnityEngine.Debug.Log("ADB push output:\n" + output);
                if (!string.IsNullOrEmpty(error))
                {
                    UnityEngine.Debug.LogError("ADB push error:\n" + error);
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("ADBコマンドの実行に失敗しました: " + e.Message);
        }
    }

    /// <summary>
    /// プレハブを登録しておく
    /// </summary>
    /// <param name="prefabPath"></param>
    private static void AddPrefabToAddressables(string prefabPath)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("AddressableAssetSettings が見つかりません");
            return;
        }

        // デフォルトグループ（必要なら専用グループでもOK）
        AddressableAssetGroup group = settings.DefaultGroup;

        string guid = AssetDatabase.AssetPathToGUID(prefabPath);

        // すでに登録済みなら何もしない
        AddressableAssetEntry entry = settings.FindAssetEntry(guid);
        if (entry == null)
        {
            entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = Path.GetFileNameWithoutExtension(prefabPath);

            Debug.Log($"Addressables に Prefab 登録: {entry.address}");
        }
    }
}
#endif
