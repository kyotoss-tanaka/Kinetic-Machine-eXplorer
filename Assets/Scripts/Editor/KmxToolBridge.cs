#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 開いている Unity で、KMXTool からの依頼（初期プロジェクト作成・線画ナビの書き出し）を受け付ける。
/// Unity を開いたままでも KMXTool から使えるようにするため（開いていなければ KMXTool が Unity を起動する）。
/// やり取りはプロジェクトの Temp\KmxTool のファイル（Temp は Unity を閉じると消えるので、古い依頼が残らない）：
///   alive.txt    （Unity → KMXTool）：受け付けている印。1 秒ごとに書き直す
///   busy.txt     （Unity → KMXTool）：依頼を処理している間だけある
///   request.json （KMXTool → Unity）：kind（newprod / lineart）・workDir（作業フォルダ）・lineart なら mechId・outDir・width
/// 結果と進み具合は作業フォルダに書く（Unity を起動して行うときと同じ形）：
///   newprod：stage1.json・go.json・stage2.json・unity.log（KmxNewProdPipeline）
///   lineart：result.json（ok・error）・unity.log（[LineArt] の行）
/// 再生中・コンパイル中は受け付けない（再生中は断る。コンパイル中は終わるまで待つ）。
/// </summary>
[InitializeOnLoad]
public static class KmxToolBridge
{
    /// <summary>依頼を処理している（KmxNewProdPipeline も立てる）</summary>
    public static bool busy;

    static double next;

    static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "KmxTool");

    static KmxToolBridge()
    {
        // Unity を起動して行うとき（-executeMethod）は起動した処理が仕事をするので、受け付けない
        if (Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-executeMethod") >= 0)
        {
            return;
        }
        EditorApplication.update += Update;
    }

    static void Update()
    {
        if (EditorApplication.timeSinceStartup < next)
        {
            return;
        }
        next = EditorApplication.timeSinceStartup + 1;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "alive.txt"), DateTime.Now.ToString("o"));
            string busyFile = Path.Combine(Dir, "busy.txt");
            if (busy != File.Exists(busyFile))
            {
                if (busy)
                {
                    File.WriteAllText(busyFile, DateTime.Now.ToString("o"));
                }
                else
                {
                    File.Delete(busyFile);
                }
            }
            string requestFile = Path.Combine(Dir, "request.json");
            if (busy || !File.Exists(requestFile) || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }
            string text = File.ReadAllText(requestFile, Encoding.UTF8);
            File.Delete(requestFile);
            Accept(text);
        }
        catch (IOException)
        {
            // KMXTool が書いている最中など。次に読む
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    static void Accept(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        string kind = root.GetProperty("kind").GetString();
        string workDir = root.GetProperty("workDir").GetString();
        // 断るときは、KMXTool が最初に待つファイルに書く
        string firstStage = kind == "newprod" ? "stage1.json" : "result.json";
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            WriteResult(workDir, firstStage, false, "Unity が再生中です。再生を止めてから、やり直してください");
            return;
        }
        Debug.Log($"[KmxToolBridge] KMXTool からの依頼：{kind}（{workDir}）");
        busy = true;
        if (kind == "newprod")
        {
            // 依頼を読んだ処理の中で重い処理をしない
            EditorApplication.delayCall += () => KmxNewProdPipeline.RunInEditor(workDir);
        }
        else if (kind == "lineart")
        {
            string mechId = root.GetProperty("mechId").GetString();
            string outDir = root.GetProperty("outDir").GetString();
            int width = root.TryGetProperty("width", out var w) ? w.GetInt32() : 2000;
            EditorApplication.delayCall += () => RunLineArt(workDir, mechId, outDir, width);
        }
        else
        {
            busy = false;
            WriteResult(workDir, firstStage, false, "知らない依頼です: " + kind);
        }
    }

    /// <summary>
    /// 線画ナビの書き出し（Unity を起動して行うときの LineArtNavigationExporter.ExportFromCommandLine と同じ）
    /// </summary>
    static void RunLineArt(string workDir, string mechId, string outDir, int width)
    {
        string logFile = Path.Combine(workDir, "unity.log");
        Application.LogCallback onLog = (message, stack, type) =>
        {
            if (message.StartsWith("[LineArt]") || type == LogType.Exception || type == LogType.Error)
            {
                try
                {
                    File.AppendAllText(logFile, message.Split('\n')[0] + "\n", new UTF8Encoding(false));
                }
                catch (IOException)
                {
                }
            }
        };
        Application.logMessageReceived += onLog;
        try
        {
            var settings = PrefabLineArtRenderer.ReadSettings(new[] { "-lineArtWidth", width.ToString() });
            settings.azimuths = new[] { 45f, 135f, 225f, 315f };
            LineArtNavigationExporter.Export(mechId, outDir, settings);
            WriteResult(workDir, "result.json", true, "");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            WriteResult(workDir, "result.json", false, e.Message);
        }
        finally
        {
            Application.logMessageReceived -= onLog;
            busy = false;
        }
    }

    /// <summary>途中のファイルを読まれないよう、別名で書いてから置き換える</summary>
    static void WriteResult(string workDir, string name, bool ok, string error)
    {
        string path = Path.Combine(workDir, name);
        var data = new Dictionary<string, object> { ["ok"] = ok, ["error"] = error };
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        File.Move(path + ".tmp", path);
    }
}
#endif
