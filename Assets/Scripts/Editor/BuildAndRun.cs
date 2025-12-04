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
using static Meta.XR.MRUtilityKit.Data;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using Unity.VisualScripting;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

public class BuildAndRun
{

    private static string serverPath = "ServerData";

    private static string bundlePath = "StandaloneWindows64";

    class BuildConfig
    {
        public string target;
        public string outputPath;
        public List<string> scenes;
        public string buildOptions;
    }

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

            build.isRelease = false;
            BuildAndRunProcess(build, false);

            build.isRelease = true;
            BuildAndRunProcess(build, true);
        }
        catch
        {
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
            build.isRelease = false;
            BuildAndRunProcess(build, false, true);

            build.isRelease = true;
            BuildAndRunProcess(build, true, true);
        }
        catch
        {
        }
    }

    /*
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
            BuildAndRunProcess(build, true);
        }
        catch
        {
        }
    }
    */

    /// <summary>
    /// ビルド処理
    /// </summary>
    private static void BuildAndRunProcess(Parameters.BuildConfig build, bool isOpen, bool isProd = false)
    {
        var productName = build.isMaster ? "KMXMaster" : (build.isMR ? $"{build.mechId}_{build.name}(MR)" : build.isVR ? $"{build.mechId}_{build.name}(VR)" : $"{build.mechId}_{build.name}");
        var productDir = Path.Combine(Path.Combine(Path.Combine("Builds", build.isVR || build.isMR ? "Android" : "Windows"), build.isRelease ? "Release" : "Debug"), productName);

        BuildConfig config = new BuildConfig
        {
            target = build.isVR || build.isMR ? "Android" : "Windows",
            outputPath = build.isMR || build.isVR ? $"{productDir}.apk" : $"{productDir}/KMX.exe",
            scenes = new List<string> { scenePath },
            buildOptions = build.isRelease ? "None" : "Development"
        };

        // シーン読み込み
        SwitchBuild(build);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ビルドターゲットの変換
        if (!TryParseTarget(config.target, out BuildTarget target, out BuildTargetGroup group))
        {
            Debug.LogError("不正なビルドターゲット: " + config.target);
            return;
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
            options = (isOpen ? BuildOptions.AutoRunPlayer : BuildOptions.None) | ParseBuildOptions(config.buildOptions) | BuildOptions.CompressWithLz4HC
        };

        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        EditorUserBuildSettings.development = !build.isRelease;

        // Addressable設定
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 1;
        AddressableAssetGroupSchema sc = settings.DefaultGroup.Schemas.Find(d => d.GetType() == typeof(BundledAssetGroupSchema));
        var buildName = ((BundledAssetGroupSchema)sc).BuildPath.GetName(settings);
        var loadName = ((BundledAssetGroupSchema)sc).LoadPath.GetName(settings);
        var savePath = $"{serverPath}/{bundlePath}";
        var loadPath = $"{serverPath}/{bundlePath}";
        settings.profileSettings.SetValue(settings.activeProfileId, buildName, savePath);
        settings.profileSettings.SetValue(settings.activeProfileId, loadName, loadPath);
        // 保存
        AssetDatabase.SaveAssets();

        // プログラム実行
        BuildPipeline.BuildPlayer(options);

        // Addressable設定戻す
        settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 0;
        // 保存
        AssetDatabase.SaveAssets();

        string projectPath = Directory.GetParent(Application.dataPath).FullName;
        string folderPath = Path.Combine(projectPath, productDir);

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
        if (isOpen)
        {
            // エクスプローラーで開く
            System.Diagnostics.Process.Start("explorer.exe", folderPath);
            // タイトル、メッセージ、ボタン名
            EditorUtility.DisplayDialog("情報", "ビルドが完了しました。", "OK");
        }
    }


    [MenuItem("Kyotoss/Switch to Windows Config", false, 101)]
    public static void SwitchToWindowsConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { });
    }

    [MenuItem("Kyotoss/Switch and Run VR Config", false, 102)]
    public static void SwitchToVRConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { isVR = true });
    }

    [MenuItem("Kyotoss/Switch and Run MR Config", false, 103)]
    public static void SwitchToMRConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { isMR = true });
    }

    static void SwitchBuild(Parameters.BuildConfig build)
    {
        // シーン読み込み
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // オブジェクトを検索
        var windwosSetting = FindInScene(scene, "WindowsSetting");
        var vrSetting = FindInScene(scene, "VRSetting");
        var mrSetting = FindInScene(scene, "MRSetting");
        windwosSetting.SetActive(!build.isVR && !build.isMR);
        vrSetting.SetActive(build.isVR);
        mrSetting.SetActive(build.isMR);

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
}
#endif
