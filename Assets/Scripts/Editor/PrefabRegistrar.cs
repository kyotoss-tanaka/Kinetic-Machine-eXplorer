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
using UnityEditor.PixyzPlugin4Unity;
using UnityEditor.PixyzPlugin4Unity.Actions;
using UnityEngine;
using static Meta.WitAi.Data.AudioEncoding;

public class AddressablePrefabRegistrar
{
    private static bool isProcessing = false;

    private static  string serverPath = "ServerData";

    private static string bundlePath = "StandaloneWindows64";

    [MenuItem("Kyotoss/Register Prefab to Addressables", false , 11)]
    public static void RegisterPrefabToAddressables()
    {
        isProcessing = true;
        // ダイアログを開いて、OK時にコールバックで処理を行う
        InputDialogWindow.Show("Register Prefab to Addressables", "Addressablesに登録するフォルダを入力してください：", Path.GetFullPath(Path.Combine(Application.dataPath, "3DModels")), (string input) =>
        {
            if (string.IsNullOrEmpty(input))
            {
                isProcessing = false;
                return;
            }
            RegisterProcess(input);
        });
    }


    // validate関数（trueなら有効、falseなら無効）
    [MenuItem("Kyotoss/Register Prefab to Addressables", true)]
    private static bool ValidateRegisterPrefabToAddressables()
    {
        return !isProcessing; // 処理中なら無効化
    }

    /// <summary>
    /// 登録処理
    /// </summary>
    /// <param name="folder"></param>
    private static void RegisterProcess(string folder)
    {
        try
        {
            folder = Path.GetFullPath(folder);
            if (Directory.Exists(folder))
            {
                // Addressables 設定を取得
                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

                // 製番取得
                var prodNo = Path.GetFileName(Path.GetFullPath(folder));

                // プレハブファイル取得
                var files = Directory.GetFiles(folder).ToList().FindAll(d => Path.GetExtension(d) ==  ".prefab");

                if (files.Count > 0)
                {
                    // 設定作成
                    settings.DefaultGroup.Settings.ActivePlayModeDataBuilderIndex = 1;
                    AddressableAssetGroupSchema sc = settings.DefaultGroup.Schemas.Find(d => d.GetType() == typeof(BundledAssetGroupSchema));
                    var buildName = ((BundledAssetGroupSchema)sc).BuildPath.GetName(settings);
                    var loadName = ((BundledAssetGroupSchema)sc).LoadPath.GetName(settings);
                    var savePath = $"{serverPath}/{prodNo}/{bundlePath}";
                    var loadPath = $"{serverPath}/{prodNo}/{bundlePath}";

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