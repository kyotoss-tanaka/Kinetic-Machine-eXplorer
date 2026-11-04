#if UNITY_EDITOR
using Parameters;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 線画の導線一式（全体図→プレハブ→ユニット）を書き出す画面
/// </summary>
public class LineArtNavigationWindow : EditorWindow
{
    const string PrefKey = "KMX.LineArtNavigation.";

    string[] mechIds = new string[0];
    int mechIndex;
    string azimuths;
    int width;
    string outRoot;

    [MenuItem("Kyotoss/Export Line Art Navigation", false, 31)]
    static void Open()
    {
        GetWindow<LineArtNavigationWindow>("線画の導線");
    }

    void OnEnable()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Datas", "PrefabInfo.json");
            mechIds = JsonSerializer.Deserialize<PrefabSetting[]>(File.ReadAllText(path))
                .Select(d => d.mechId).Distinct().ToArray();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LineArt] PrefabInfo が読めません: " + e.Message);
        }
        // 既定は斜めの4方向（真横・真正面の 0/90/180/270° は分かりにくかったので使わない。保存キーを変えたので以前の設定は引き継がない）
        azimuths = EditorPrefs.GetString(PrefKey + "azimuths4", "45,135,225,315");
        width = EditorPrefs.GetInt(PrefKey + "width", 2000);
        outRoot = EditorPrefs.GetString(PrefKey + "outRoot",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KMX_LineArt"));
    }

    void OnGUI()
    {
        if (mechIds.Length == 0)
        {
            EditorGUILayout.HelpBox("PrefabInfo に機番がありません", MessageType.Warning);
            return;
        }
        mechIndex = EditorGUILayout.Popup("機番", Mathf.Clamp(mechIndex, 0, mechIds.Length - 1), mechIds);
        azimuths = EditorGUILayout.TextField("見る方向(°, カンマ区切り)", azimuths);
        width = EditorGUILayout.IntField("画像の幅(px)", width);
        using (new EditorGUILayout.HorizontalScope())
        {
            outRoot = EditorGUILayout.TextField("保存先", outRoot);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFolderPanel("保存先", outRoot, "");
                if (!string.IsNullOrEmpty(picked)) outRoot = picked;
            }
        }
        string mechId = mechIds[mechIndex];
        EditorGUILayout.LabelField("書き出し先", Path.Combine(outRoot, mechId));

        if (GUILayout.Button("書き出す", GUILayout.Height(28)))
        {
            EditorPrefs.SetString(PrefKey + "azimuths4", azimuths);
            EditorPrefs.SetInt(PrefKey + "width", width);
            EditorPrefs.SetString(PrefKey + "outRoot", outRoot);
            var settings = new PrefabLineArtRenderer.Settings
            {
                azimuths = Array.ConvertAll(azimuths.Split(','), float.Parse),
                width = width,
            };
            string outDir = Path.Combine(outRoot, mechId);
            try
            {
                LineArtNavigationExporter.Export(mechId, outDir, settings);
                EditorUtility.RevealInFinder(Path.Combine(outDir, "manifest.json"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
