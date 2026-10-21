#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
//using UnityEditor.PixyzPlugin4Unity;
//using UnityEditor.PixyzPlugin4Unity.Actions;

public class AddressablePrefabRegistrar
{
    private static bool isProcessing = false;

    private static bool isVR = false;

    private static  string serverPath = "ServerData";

    private static string bundlePath = "StandaloneWindows64";

    [MenuItem("Kyotoss/Register Prefab to Addressables", false , 15)]
    public static void RegisterPrefabToAddressables()
    {
        isProcessing = true;
        // ダイアログを開いて、OK時にコールバックで処理を行う
        /*
        InputDialogWindow.Show("Register Prefab to Addressables", "Addressablesに登録するフォルダを入力してください：", Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels")), (string input) =>
        {
            if (string.IsNullOrEmpty(input))
            {
                isProcessing = false;
                return;
            }
            isVR = false;
            RegisterProcess(input);
        });
        */
        string path = EditorUtility.OpenFolderPanel("Addressablesに登録するフォルダを選択", Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels")), "");
        if (path != "")
        {
            isVR = false;
            RegisterProcess(path);
        }
        isProcessing = false;
    }

    // validate関数（trueなら有効、falseなら無効）
    [MenuItem("Kyotoss/Register Prefab to Addressables", true)]
    private static bool ValidateRegisterPrefabToAddressables()
    {
        return !isProcessing; // 処理中なら無効化
    }

    /// <summary>
    /// 複数フォルダのプレハブを一つのカタログに登録する（二つのプロジェクトを統合した時など）。
    /// 別々に作った二つのカタログは同じ名前の bundle（monoscripts）を含むため同時に読めない。まとめて作れば一つで済む。
    /// 製番（出力フォルダ名・ProdNo.txt）は選んだフォルダ名を「_」でつないだ物（例：G822_G823）
    /// </summary>
    [MenuItem("Kyotoss/Register Prefab to Addressables (Multiple Folders)", false, 16)]
    public static void RegisterPrefabsOfFoldersToAddressables()
    {
        isProcessing = true;
        var folders = new List<string>();
        var start = Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels"));
        while (true)
        {
            string path = EditorUtility.OpenFolderPanel($"Addressablesに登録するフォルダを選択（{folders.Count + 1}個目）", start, "");
            if (path == "")
            {
                break;
            }
            path = Path.GetFullPath(path);
            if (!folders.Contains(path))
            {
                folders.Add(path);
            }
            start = Path.GetDirectoryName(path);
            var list = string.Join("\n", folders.Select(d => "・" + Path.GetFileName(d)));
            var answer = EditorUtility.DisplayDialogComplex("Register Prefab to Addressables (Multiple Folders)",
                $"選択したフォルダ：\n{list}\n\n続けてフォルダを追加しますか？", "追加する", "やめる", "登録する");
            if (answer == 1)
            {
                // やめる
                folders.Clear();
                break;
            }
            if (answer == 2)
            {
                // 登録する
                break;
            }
        }
        if (folders.Count > 0)
        {
            isVR = false;
            RegisterProcess(folders, string.Join("_", folders.Select(d => Path.GetFileName(d))));
        }
        isProcessing = false;
    }

    // validate関数（trueなら有効、falseなら無効）
    [MenuItem("Kyotoss/Register Prefab to Addressables (Multiple Folders)", true)]
    private static bool ValidateRegisterPrefabsOfFoldersToAddressables()
    {
        return !isProcessing; // 処理中なら無効化
    }

#if false
    [MenuItem("Kyotoss/Register Prefab to Addressables(VR)", false, 12)]
    public static void RegisterPrefabToAddressablesVR()
    {
        isProcessing = true;
        /*
        // ダイアログを開いて、OK時にコールバックで処理を行う
        InputDialogWindow.Show("Register Prefab to Addressables(VR)", "Addressablesに登録するフォルダを入力してください：", Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels")), (string input) =>
        {
            if (string.IsNullOrEmpty(input))
            {
                isProcessing = false;
                return;
            }
            isVR = true;
            RegisterProcess(input);
        });
        */

        string path = EditorUtility.OpenFolderPanel("Addressablesに登録するフォルダを選択(VR)", Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels")), "");
        if (path != "")
        {
            isVR = true;
            RegisterProcess(path);
        }
        isProcessing = false;
    }

    // validate関数（trueなら有効、falseなら無効）
    [MenuItem("Kyotoss/Register Prefab to Addressables(VR)", true)]
    private static bool ValidateRegisterPrefabToAddressablesVR()
    {
        return !isProcessing; // 処理中なら無効化
    }
#endif

    /// <summary>
    /// 登録処理
    /// </summary>
    /// <param name="folder"></param>
    private static void RegisterProcess(string folder)
    {
        folder = Path.GetFullPath(folder);
        // 製番はフォルダ名
        RegisterProcess(new List<string> { folder }, Path.GetFileName(folder));
    }

    /// <summary>
    /// 登録処理（複数フォルダのプレハブを一つのカタログにする）
    /// </summary>
    /// <param name="folders">プレハブのフォルダ</param>
    /// <param name="prodNo">製番（出力フォルダ名・ProdNo.txt）</param>
    private static void RegisterProcess(List<string> folders, string prodNo)
    {
        try
        {
            folders = folders.Select(d => Path.GetFullPath(d)).ToList();
            if (folders.All(d => Directory.Exists(d)))
            {
                // Addressables 設定を取得
                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

                // プレハブファイル取得
                var files = folders.SelectMany(folder => Directory.GetFiles(isVR ? Path.Combine(folder, "VR") : folder).ToList().FindAll(d => Path.GetExtension(d) == ".prefab")).ToList();
                // アドレスはファイル名なので、同じ名前のプレハブが別のフォルダにあると取り違える
                var dupNames = files.GroupBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dupNames.Count > 0)
                {
                    EditorUtility.DisplayDialog("エラー", "同じ名前のプレハブが複数のフォルダにあります：\n" + string.Join("\n", dupNames), "OK");
                    isProcessing = false;
                    return;
                }
                if (files.Count > 0)
                {
                    // 設定作成
                    settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 1;
                    AddressableAssetGroupSchema sc = settings.DefaultGroup.Schemas.Find(d => d.GetType() == typeof(BundledAssetGroupSchema));
                    var buildName = ((BundledAssetGroupSchema)sc).BuildPath.GetName(settings);
                    var loadName = ((BundledAssetGroupSchema)sc).LoadPath.GetName(settings);
                    var vrParh = isVR ? "VR/" : "";
                    var savePath = $"{serverPath}/{prodNo}/{vrParh}{bundlePath}";
                    var loadPath = $"{serverPath}/{prodNo}/{vrParh}{bundlePath}";

                    settings.profileSettings.SetValue(settings.activeProfileId, buildName, savePath);
                    settings.profileSettings.SetValue(settings.activeProfileId, loadName, loadPath);

                    // Prefab削除
                    var entriesToRemove = settings.DefaultGroup.entries.ToList().FindAll(entry =>
                    {
                        string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                        return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                    });
                    foreach (var entry in entriesToRemove)
                    {
                        settings.DefaultGroup.RemoveAssetEntry(entry);
                    }

                    // Prefabを順番に登録
                    files.Sort((a, b) => a.CompareTo(b));
                    int total = files.Count;
                    for (int i = 0; i < total; i++)
                    {
                        string file = files[i];
                        // Prefab の GUID を取得// 絶対パスから Assets 相対パスに変換
                        string assetPath = file.Replace("\\", "/").Replace(Application.dataPath, "Assets");
                        string assetGUID = AssetDatabase.AssetPathToGUID(assetPath);
                        if (string.IsNullOrEmpty(assetGUID))
                        {
                            continue;
                        }
                        // 既に登録済みか確認
                        AddressableAssetEntry entry = settings.CreateOrMoveEntry(assetGUID, settings.DefaultGroup);
                        entry.SetAddress(Path.GetFileNameWithoutExtension(file));
                        entry.SetLabel("Prefab", true, true);

                        // 進捗表示
                        float progress = (float)(i + 1) / total;
                        EditorApplication.delayCall += () =>
                        {
                            EditorUtility.DisplayProgressBar("Addressables登録中", $"{i + 1}/{total} Prefab登録中...", progress);
                        };
                    }
                    // 保存
                    AssetDatabase.SaveAssets();
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryRemoved, null, true);

                    EditorApplication.delayCall += () =>
                    {
                        EditorUtility.ClearProgressBar();
                        AddressableAssetSettings.BuildPlayerContent();

                        // フォルダ構成構築
                        string projectPath = Directory.GetParent(Application.dataPath).FullName;
                        string basePath = Path.Combine(Path.Combine(Path.Combine(projectPath, "Library"), "com.unity.addressables"), "aa");
                        string dataPath = Path.Combine(projectPath, savePath);
                        string folderPath = Path.Combine(basePath, prodNo);
                        string addressablesPath = Path.Combine(basePath, "Windows");
                        if (Directory.Exists(addressablesPath))
                        {
                            if (Directory.Exists(folderPath))
                            {
                                //　存在したら一度削除
                                Directory.Delete(folderPath, true);
                            }
                            Directory.CreateDirectory(folderPath);
                            foreach (var file in files)
                            {
                                // プレハブファイルをコピー
                                var name = Path.GetFileName(file);
                                File.Copy(file, Path.Combine(folderPath, name));
                            }
                            var targetPath = Path.Combine(folderPath, "Addressables");
                            Directory.Move(addressablesPath, targetPath);
                            File.WriteAllText(Path.Combine(targetPath, "ProdNo.txt"), prodNo);

                            targetPath = Path.Combine(targetPath, "StandaloneWindows64");
                            Directory.CreateDirectory(targetPath);
                            foreach (var file in Directory.GetFiles(dataPath))
                            {
                                File.Copy(file, Path.Combine(targetPath, Path.GetFileName(file)), true);
                            }
                            // タイトル、メッセージ、ボタン名
                            EditorUtility.DisplayDialog("完了", "PrefabのAddressables登録が完了しました。", "OK");

                            // ビルドフォルダを開く
                            Process.Start("explorer.exe", folderPath);
                        }
                        // 設定を戻す
                        settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 0;

                        // Prefab削除
                        var entriesToRemove = settings.DefaultGroup.entries.ToList().FindAll(entry =>
                        {
                            string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                            return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
                        });
                        foreach (var entry in entriesToRemove)
                        {
                            settings.DefaultGroup.RemoveAssetEntry(entry);
                        }
                        // 保存
                        AssetDatabase.SaveAssets();

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
        catch (Exception ex)
        {
            EditorApplication.delayCall += () =>
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("エラー", ex.Message, "OK");
                isProcessing = false;
            };
        }
    }
}
#endif