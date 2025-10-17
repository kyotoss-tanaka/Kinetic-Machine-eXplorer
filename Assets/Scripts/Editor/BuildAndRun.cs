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
using MongoDB.Bson;

public class BuildAndRun
{
    class BuildConfig
    {
        public string target;
        public string outputPath;
        public List<string> scenes;
        public string buildOptions;
    }

    static string scenePath = "Assets/Scenes/Simuration.unity";

    [MenuItem("Kyotoss/Build and Run from KMXTool Config", false, 1)]
    public static void BuildAndRunFromConfig()
    {
        try
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                UnityEngine.Debug.Log("シーンを保存しました。");
            }

            string configPath = Path.Combine("Assets/StreamingAssets/Datas", "BuildConfig.json");
            if (!File.Exists(configPath))
            {
                Debug.LogError("設定ファイルが見つかりません: " + configPath);
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            Parameters.BuildConfig build = JsonSerializer.Deserialize<Parameters.BuildConfig>(json);

            var productName = build.isMR ? $"{build.mechId}_{build.name}(MR)" : build.isVR ? $"{build.mechId}_{build.name}(VR)" : $"{build.mechId}_{build.name}";

            BuildConfig config = new BuildConfig
            {
                target = build.isVR || build.isMR ? "Android" : "Windows",
                outputPath = build.isMR || build.isVR ? $"Builds/Android/{productName}.apk" : $"Builds/Windows/{productName}/KMX.exe",
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
                options = BuildOptions.AutoRunPlayer | ParseBuildOptions(config.buildOptions) | BuildOptions.CompressWithLz4HC
            };

            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = !build.isRelease;

            BuildPipeline.BuildPlayer(options);
        }
        catch
        {
        }
    }


    [MenuItem("Kyotoss/Switch to Windows Config", false, 51)]
    public static void SwitchToWindowsConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { });
    }

    [MenuItem("Kyotoss/Switch and Run VR Config", false, 52)]
    public static void SwitchToVRConfig()
    {
        SwitchBuild(new Parameters.BuildConfig { isVR = true });
    }

    [MenuItem("Kyotoss/Switch and Run MR Config", false, 53)]
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
