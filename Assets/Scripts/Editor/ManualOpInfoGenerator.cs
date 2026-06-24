using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Parameters;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 「動作するユニット」から手動操作(JOG)定義 ManualOpInfo.json を自動生成する。
///
/// 対象: ActionInfo の mode 0-3（直線/回転/外部直線/外部回転）＋ RobotInfo（各軸）。
/// JOGデバイス(write): 各ユニットの軸方向に HMX 手動操作用 内部IO(IB9600〜IB9799) を割り当てる。
/// ランプデバイス(read): JOG内部IO + 200（IB9800〜IB9999）。PLCがボタン認識を返す→KMXが購読して読みボタンを点灯。
///   ※ IB9500+ は HMX 認可領域と衝突するため不可。手動操作専用は IB9600-9999（JOG=9600-9799 / ランプ=9800-9999）。
///   ※ 内部IO ↔ 実デバイス/インターロック/ランプ の紐づけは HMIプロジェクト側(manualOpMap)で機械別に定義。
///   ※ 生成された ManualOpInfo.json は「ユニット/軸 → 内部IO(JOG/ランプ)」対応表として HMX と共有する。
///
/// 採番は安定（同一ユニット/同一軸方向＝常に同じ内部IO）。
///   既存 ManualOpInfo.json の IB 割当を引き継ぎ、新規キーだけ未使用の最小IBを割り当てる。
///   ＝機種更新で番号がズレて HMX の実デバイス割付が崩れるのを防ぐ（HMX依頼事項 §9.4）。
///
/// 実行: メニュー「Kyotoss/Generate ManualOpInfo (内部IO割当)」 or WebGLビルド前に自動。
/// </summary>
public static class ManualOpInfoGenerator
{
    private const int IbStart = 9600;        // 手動操作 JOG(write) 内部IO 開始（IB9600〜）
    private const int IbEnd = 9799;          // 〃 終了（手動操作予約 IB9600-9799 = 200個）
    private const int LampOffset = 200;      // ランプ(read) 内部IO = JOG内部IO + 200（IB9800-9999）
    private const string IbPrefix = "IB";

    [MenuItem("Kyotoss/Generate ManualOpInfo (内部IO割当)")]
    public static void GenerateMenu()
    {
        Generate(true);
    }

    public static void Generate(bool log)
    {
        string dir = Path.Combine(Application.streamingAssetsPath, "Datas");
        string outPath = Path.Combine(dir, "ManualOpInfo.json");

        var actions = LoadList<UnitActionSetting>(Path.Combine(dir, "ActionInfo.json"));
        var robots = LoadList<RobotSetting>(Path.Combine(dir, "RobotInfo.json"));
        var units = LoadList<UnitSetting>(Path.Combine(dir, "UnitInfo.json"));   // group/path 解決用（親子）

        // 既存割当を引き継ぐ（安定採番）: 既存 ManualOpInfo の IB(範囲内のみ) を key→dev として再利用
        var keyToDev = new Dictionary<string, string>();
        var usedNums = new HashSet<int>();
        var existing = LoadList<ManualOpData>(outPath);
        if (existing != null)
        {
            foreach (var u in existing)
            {
                if (u == null || u.ops == null)
                {
                    continue;
                }
                foreach (var op in u.ops)
                {
                    if (op == null)
                    {
                        continue;
                    }
                    int n = ParseIb(op.dev);
                    if (n >= IbStart && n <= IbEnd)   // 範囲内のIBのみ引継ぎ（旧サンプルの実デバイス等は無視）
                    {
                        keyToDev[Key(u.mechId, u.name, op.axis, op.dir)] = op.dev;
                        usedNums.Add(n);
                    }
                }
            }
        }

        var result = new List<ManualOpData>();
        int exhausted = 0;

        // 直線/回転（ActionInfo mode 0-3）
        if (actions != null)
        {
            foreach (var a in actions)
            {
                if (a == null || !(a.mode == 0 || a.mode == 1 || a.mode == 2 || a.mode == 3))
                {
                    continue;
                }
                bool rotate = (a.mode == 1 || a.mode == 3);
                int ax = Mathf.Clamp(a.axis, 0, 2);
                var data = GetOrAdd(result, a.mechId, a.name);
                AddOp(data, a.mechId, a.name, ax, 1, rotate ? "正転" : "前進", keyToDev, usedNums, ref exhausted);
                AddOp(data, a.mechId, a.name, ax, -1, rotate ? "逆転" : "後退", keyToDev, usedNums, ref exhausted);
            }
        }

        // ロボット（RobotInfo の各軸 +/-）
        if (robots != null)
        {
            foreach (var r in robots)
            {
                if (r == null || r.tags == null)
                {
                    continue;
                }
                var data = GetOrAdd(result, r.mechId, r.name);
                for (int i = 0; i < r.tags.Count; i++)
                {
                    string t = r.tags[i];
                    if (string.IsNullOrEmpty(t))
                    {
                        continue;
                    }
                    int ax = AxisFromTag(t, i);
                    string an = AxisName(ax);
                    AddOp(data, r.mechId, r.name, ax, 1, an + "＋", keyToDev, usedNums, ref exhausted);
                    AddOp(data, r.mechId, r.name, ax, -1, an + "－", keyToDev, usedNums, ref exhausted);
                }
            }
        }

        // group / path（論理親子）を UnitInfo の children 階層から付与（parentフィールド=プレハブ名は使わない）
        AssignGroups(result, units);

        WriteJson(outPath, result);
        AssetDatabase.Refresh();
        if (log)
        {
            int opCount = 0;
            foreach (var u in result)
            {
                opCount += u.ops.Count;
            }
            Debug.Log($"[ManualOpInfo] 生成: {result.Count}ユニット / {opCount}op (JOG IB{IbStart}-{IbEnd} / ランプ IB{IbStart + LampOffset}-{IbEnd + LampOffset})");
            if (exhausted > 0)
            {
                Debug.LogError($"[ManualOpInfo] 内部IO({IbStart}-{IbEnd}={IbEnd - IbStart + 1}個)を超過し {exhausted}件 未割当(dev空)。範囲拡張をHMXと協議要。");
            }
        }
    }

    private static void AddOp(ManualOpData data, string mechId, string name, int axis, int dir, string label,
        Dictionary<string, string> keyToDev, HashSet<int> usedNums, ref int exhausted)
    {
        string key = Key(mechId, name, axis, dir);
        if (!keyToDev.TryGetValue(key, out string dev))
        {
            int n = NextFree(usedNums);
            if (n < 0)
            {
                exhausted++;
                dev = "";   // 範囲超過（要範囲拡張）
            }
            else
            {
                dev = IbPrefix + n;
                usedNums.Add(n);
                keyToDev[key] = dev;
            }
        }
        data.ops.Add(new ManualOp { axis = axis, dir = dir, label = label, dev = dev, lamp = LampFor(dev), tag = "", onValue = 1, mode = "jog" });
    }

    /// <summary>各ユニットに group(最上位の論理親)と path(最上位→自分) を付与。
    /// 論理階層は UnitInfo の **children**（親ユニット→子ユニット）に入っている。
    /// ※ `parent` フィールドはプレハブ/CADモデルの親オブジェクト名なので使わない（部品番号等が出てしまう）。</summary>
    private static void AssignGroups(List<ManualOpData> result, List<UnitSetting> units)
    {
        // children から「子→親」を構築（mechId|子ユニット名 → 親ユニット名）
        var parentOf = new Dictionary<string, string>();
        if (units != null)
        {
            foreach (var u in units)
            {
                if (u == null || u.children == null || string.IsNullOrEmpty(u.name))
                {
                    continue;
                }
                foreach (var c in u.children)
                {
                    if (c != null && !string.IsNullOrEmpty(c.name))
                    {
                        parentOf[u.mechId + "|" + c.name] = u.name;
                    }
                }
            }
        }
        foreach (var d in result)
        {
            var chain = new List<string>();
            var seen = new HashSet<string>();
            string cur = d.name;
            int guard = 0;
            while (!string.IsNullOrEmpty(cur) && guard++ < 64 && seen.Add(cur))
            {
                chain.Add(cur);   // self → up
                parentOf.TryGetValue(d.mechId + "|" + cur, out string p);
                cur = p;
            }
            chain.Reverse();      // top → self
            d.path = chain;
            d.group = chain.Count > 0 ? chain[0] : d.name;   // 最上位（親なしは自分自身）
        }
    }

    /// <summary>ランプ読取デバイス＝JOG内部IO+LampOffset（IB9800-9999）。dev が範囲外/空なら空。</summary>
    private static string LampFor(string jogDev)
    {
        int n = ParseIb(jogDev);
        if (n < IbStart || n > IbEnd)
        {
            return "";
        }
        return IbPrefix + (n + LampOffset);
    }

    private static int NextFree(HashSet<int> used)
    {
        for (int n = IbStart; n <= IbEnd; n++)
        {
            if (!used.Contains(n))
            {
                return n;
            }
        }
        return -1;
    }

    private static string Key(string mechId, string name, int axis, int dir)
    {
        return $"{mechId}|{name}|{axis}|{dir}";
    }

    private static int ParseIb(string dev)
    {
        if (string.IsNullOrEmpty(dev) || !dev.StartsWith(IbPrefix))
        {
            return -1;
        }
        return int.TryParse(dev.Substring(IbPrefix.Length), out int n) ? n : -1;
    }

    private static ManualOpData GetOrAdd(List<ManualOpData> list, string mechId, string name)
    {
        var d = list.Find(x => x.mechId == mechId && x.name == name);
        if (d == null)
        {
            d = new ManualOpData { mechId = mechId, name = name };
            list.Add(d);
        }
        return d;
    }

    private static int AxisFromTag(string tag, int idx)
    {
        if (tag.EndsWith(".x")) return 0;
        if (tag.EndsWith(".y")) return 1;
        if (tag.EndsWith(".z")) return 2;
        return Mathf.Clamp(idx, 0, 2);
    }

    private static string AxisName(int axis)
    {
        return axis == 0 ? "X" : (axis == 1 ? "Y" : "Z");
    }

    private static List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ManualOpInfo] 読込失敗 {path}: {e.Message}");
            return null;
        }
    }

    private static void WriteJson(string path, List<ManualOpData> data)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 日本語をそのまま出力
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, opts));
    }
}

/// <summary>WebGLビルド前に ManualOpInfo.json を自動生成する。</summary>
public class ManualOpInfoBuildHook : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.WebGL)
        {
            ManualOpInfoGenerator.Generate(true);
        }
    }
}
