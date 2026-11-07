#if UNITY_EDITOR
using Parameters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Part = PrefabLineArtRenderer.Part;

/// <summary>
/// KMX の設定（PrefabInfo / HiddenUnitInfo / UnitInfo）から、グループ → ユニット → 部品とたどる線画一式を書き出す
/// ・全体図：全グループ（プレハブ）。押すとグループ
/// ・グループ図：そのプレハブだけ。押すとユニット
/// ・ユニット図：そのユニットだけ。押すと部品（シリンダ・センサ・モータ等。LineArtComponentClassifier で見分ける）
/// 図ごとに線画（PNG）と、表示用のデータ（.js：線画の data URI と、画素ごとに何が写っているかの ushort [値, 個数] を base64 にした物）を書き出す。
/// データを .js にしているのは、index.html をダブルクリック（file://）で開いても読めるようにするため（fetch や画像の画素読み取りは file:// で止められる）
/// 赤い強調は表示側（index.html）で塗る。カバー（非表示設定の種類=カバーの物と、機械のアクリル等のカバー）は描かない
/// アクリル等のカバーは CAD のプロパティ（Pixyz の Metadata に SolidWorks のプロパティが入っている）の標準品名と材質で見分ける
/// </summary>
public static class LineArtNavigationExporter
{
    const string ViewerTemplate = "Assets/Scripts/Editor/LineArt/LineArtViewer.html";

    class PrefabEntry
    {
        public string name;
        public Transform root;
        public List<int> parts = new List<int>();
        public List<int> units = new List<int>();
    }

    class UnitEntry
    {
        public UnitSetting setting;
        public int prefab = -1;
        public HashSet<int> parts = new HashSet<int>();
        public List<string> childUnits = new List<string>();
        public string parentUnit;
        public List<int> comps = new List<int>();
    }

    class CompEntry
    {
        public int unit;
        public LineArtComponentClassifier.Info info;
        public List<int> parts = new List<int>();
        /// <summary>部品のノード（「Copy of」で分かれた物は複数）</summary>
        public List<Transform> nodes = new List<Transform>();
    }

    /// <summary>
    /// バッチモード用：-lineArtMech R8790 -lineArtOut &lt;フォルダ&gt; [-lineArtWidth N] [-lineArtAzimuths 45,135,225,315]（既定は斜めの4方向）
    /// </summary>
    public static void ExportFromCommandLine()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string mechId = PrefabLineArtRenderer.GetArg(args, "-lineArtMech");
            string outDir = PrefabLineArtRenderer.GetArg(args, "-lineArtOut");
            var settings = PrefabLineArtRenderer.ReadSettings(args);
            if (PrefabLineArtRenderer.GetArg(args, "-lineArtAzimuths") == null) settings.azimuths = new[] { 45f, 135f, 225f, 315f };
            Export(mechId, outDir, settings);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static List<T> LoadJson<T>(string name)
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Datas", name + ".json");
        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path));
    }

    public static void Export(string mechId, string outDir, PrefabLineArtRenderer.Settings settings)
    {
        var prefabSettings = LoadJson<PrefabSetting>("PrefabInfo").Where(d => d.mechId == mechId).ToList();
        var hiddenSettings = LoadJson<HiddenUnit>("HiddenUnitInfo");
        var unitSettings = LoadJson<UnitSetting>("UnitInfo").Where(d => d.mechId == mechId).ToList();
        if (prefabSettings.Count == 0) throw new Exception($"PrefabInfo に機番 {mechId} がありません");
        propsCache.Clear();

        // 機番の置き場所（KMX の GetMechPose と同じく最初に値のあるもの）
        var poseSetting = prefabSettings.FirstOrDefault(d => d.pos != null && d.pos.Count >= 3) ?? prefabSettings[0];
        var posePos = (poseSetting.pos != null && poseSetting.pos.Count >= 3) ? new Vector3(poseSetting.pos[0], poseSetting.pos[1], poseSetting.pos[2]) : Vector3.zero;
        var poseRot = (poseSetting.rot != null && poseSetting.rot.Count >= 3) ? Quaternion.Euler(poseSetting.rot[0], poseSetting.rot[1], poseSetting.rot[2]) : Quaternion.identity;

        // プレハブを読んで部品を集める（非表示・カバーの物は除く）
        var parts = new List<Part>();
        var prefabs = new List<PrefabEntry>();
        foreach (var ps in prefabSettings)
        {
            string name = Path.GetFileNameWithoutExtension(ps.name);
            var asset = FindPrefab(name);
            if (asset == null)
            {
                Debug.LogWarning($"[LineArt] プレハブが見つかりません: {name}");
                continue;
            }
            var root = asset.transform;
            var hidden = FindHidden(root, hiddenSettings, out int resinCovers);
            // KMX の PlacePrefab と同じ：位置は機番の位置、回転は機番の回転×元の回転
            var rootPose = Matrix4x4.TRS(posePos, poseRot * root.localRotation, root.localScale);
            int start = parts.Count;
            PrefabLineArtRenderer.CollectParts(root, rootPose, parts, hidden);
            var entry = new PrefabEntry { name = name, root = root };
            for (int i = start; i < parts.Count; i++) entry.parts.Add(i);
            prefabs.Add(entry);
            Debug.Log($"[LineArt] {name}: 部品 {entry.parts.Count} 個（非表示・カバー {hidden.Count} 件、うちアクリル等のカバー {resinCovers} 件）");
        }

        // 機械から離れた小部品（目印の点など）を外す
        var strays = PrefabLineArtRenderer.FindStrays(parts, Enumerable.Range(0, parts.Count).ToList());
        if (strays.Count > 0)
        {
            Debug.Log($"[LineArt] 離れた小部品 {strays.Count} 個を除外: {PrefabLineArtRenderer.PartNames(parts, strays)}");
            foreach (var pe in prefabs) pe.parts.RemoveAll(strays.Contains);
        }

        var partIndex = new Dictionary<Transform, int>();
        var partPrefab = new int[parts.Count];
        for (int p = 0; p < prefabs.Count; p++)
        {
            foreach (int i in prefabs[p].parts)
            {
                partIndex[parts[i].source] = i;
                partPrefab[i] = p;
            }
        }

        var units = ResolveUnits(unitSettings, prefabs, partIndex, partPrefab);
        Debug.Log($"[LineArt] 形のあるユニット {units.Count} 個（設定 {unitSettings.Count} 件）");

        // 部品ごとに「一番小さい（具体的な）ユニット」を押せる範囲にする
        var partUnit = Enumerable.Repeat(-1, parts.Count).ToArray();
        for (int u = 0; u < units.Count; u++)
        {
            foreach (int i in units[u].parts)
            {
                if (partUnit[i] < 0 || units[partUnit[i]].parts.Count > units[u].parts.Count) partUnit[i] = u;
            }
        }

        var comps = ResolveComponents(units, parts);
        Debug.Log($"[LineArt] 部品 {comps.Count} 個: " +
            string.Join(" / ", comps.GroupBy(c => c.info.type).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        // パスで一つに決まらない部品（同じ親の下に同じ名前のノードがある）
        var samePath = comps.SelectMany(c => c.nodes).Distinct().GroupBy(CadPath).Where(g => g.Count() > 1).ToList();
        if (samePath.Count > 0)
        {
            Debug.LogWarning($"[LineArt] パスが重なる部品 {samePath.Count} 件（パスでは一つに決まりません）: " +
                string.Join(" / ", samePath.Take(10).Select(g => g.Key)));
        }

        Directory.CreateDirectory(outDir);
        var viewsJson = new Dictionary<string, object>();
        try
        {
            using (var ctx = new PrefabLineArtRenderer.Context(settings))
            {
                int total = settings.azimuths.Length * (1 + prefabs.Count + units.Count), done = 0;
                foreach (float az in settings.azimuths)
                {
                    string dir = $"az{az:0}";
                    Directory.CreateDirectory(Path.Combine(outDir, dir));
                    void Progress(string what) => EditorUtility.DisplayProgressBar("線画の導線", $"{dir} {what}", (float)done++ / total);

                    // 全体図：押す単位はグループ
                    Progress("全体図");
                    var overview = RenderView(ctx, parts, prefabs.SelectMany(p => p.parts).ToList(), az, outDir, $"{dir}/overview",
                        i => partPrefab[i]);

                    // グループ図：押す単位はユニット
                    var groupViews = new List<object>();
                    for (int p = 0; p < prefabs.Count; p++)
                    {
                        Progress(prefabs[p].name);
                        var localOf = LocalIndex(prefabs[p].units);
                        groupViews.Add(RenderView(ctx, parts, prefabs[p].parts, az, outDir, $"{dir}/g{p}",
                            i => partUnit[i] >= 0 && localOf.TryGetValue(partUnit[i], out int k) ? k : -1));
                    }

                    // ユニット図：押す単位は部品
                    var unitViews = new List<object>();
                    for (int u = 0; u < units.Count; u++)
                    {
                        Progress(units[u].setting.name);
                        var compOf = new Dictionary<int, int>();
                        for (int k = 0; k < units[u].comps.Count; k++)
                        {
                            foreach (int i in comps[units[u].comps[k]].parts) compOf[i] = k;
                        }
                        unitViews.Add(RenderView(ctx, parts, units[u].parts.ToList(), az, outDir, $"{dir}/u{u}",
                            i => compOf.TryGetValue(i, out int k) ? k : -1,
                            units[u].comps.Select(c => comps[c].parts).ToList()));
                    }
                    viewsJson[$"{az:0}"] = new Dictionary<string, object>
                    {
                        ["overview"] = overview,
                        ["groups"] = groupViews,
                        ["units"] = unitViews,
                    };
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        var manifest = new Dictionary<string, object>
        {
            ["mechId"] = mechId,
            ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["azimuths"] = settings.azimuths.Select(a => $"{a:0}").ToList(),
            ["types"] = LineArtComponentClassifier.Types,
            ["groups"] = prefabs.Select(p => new Dictionary<string, object>
            {
                ["name"] = p.name,
                ["letter"] = GroupLetter(p.name),
                ["units"] = p.units,
            }).ToList(),
            ["units"] = units.Select(u => new Dictionary<string, object>
            {
                ["name"] = u.setting.name,
                ["group"] = u.prefab,
                ["parent"] = u.parentUnit,
                ["children"] = u.childUnits,
                ["sync"] = u.setting.sync,
                ["comps"] = u.comps,
            }).ToList(),
            ["comps"] = comps.Select(c => new Dictionary<string, object>
            {
                ["unit"] = c.unit,
                ["type"] = c.info.type,
                ["partNo"] = c.info.partNo,
                ["maker"] = c.info.maker,
                ["item"] = c.info.item,
                ["code"] = c.info.code,
                // CAD のツリーと同じパス（電気部品リスト等から、このパスで部品を開く）
                ["paths"] = c.nodes.Select(CadPath).Distinct().ToList(),
            }).ToList(),
            ["views"] = viewsJson,
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest));
        WriteData(Path.Combine(outDir, "manifest.js"), "manifest.js", manifest);
        if (File.Exists(ViewerTemplate))
        {
            // 表示ページ：社内の Web サーバー等にフォルダごと置けば、スマホのブラウザでも見られる
            File.WriteAllText(Path.Combine(outDir, "index.html"),
                "<!doctype html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n" +
                "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">\n</head>\n<body>\n" +
                File.ReadAllText(ViewerTemplate) + "\n</body>\n</html>\n");
        }
        Debug.Log($"[LineArt] 完了: {outDir}");
    }

    static Dictionary<int, int> LocalIndex(List<int> list)
    {
        var map = new Dictionary<int, int>();
        for (int k = 0; k < list.Count; k++) map[list[k]] = k;
        return map;
    }

    /// <summary>プレハブ名 R8790-A0-00-00 → A</summary>
    static string GroupLetter(string prefabName)
    {
        var seg = prefabName.Split('-');
        return seg.Length > 1 && seg[1].Length > 0 ? seg[1].Substring(0, 1) : prefabName;
    }

    /// <summary>
    /// 1枚の図を描く：線画（PNG）と表示用のデータ（.js）
    /// labelOfPart は部品番号→押す項目（-1 は押せない）
    /// </summary>
    static Dictionary<string, object> RenderView(PrefabLineArtRenderer.Context ctx, List<Part> parts, List<int> viewParts,
        float az, string outDir, string baseName, Func<int, int> labelOfPart, List<List<int>> xrayItems = null)
    {
        // この図だけで見て離れている小部品も外す（単体の図で範囲が広がらないように）
        var viewStrays = PrefabLineArtRenderer.FindStrays(parts, viewParts);
        if (viewStrays.Count > 0)
        {
            Debug.Log($"[LineArt] {baseName}: 離れた小部品 {viewStrays.Count} 個を除外: {PrefabLineArtRenderer.PartNames(parts, viewStrays)}");
            viewParts = viewParts.Where(i => !viewStrays.Contains(i)).ToList();
        }
        var frame = PrefabLineArtRenderer.Fit(parts, viewParts, az, ctx.settings);
        var scene = PrefabLineArtRenderer.RenderGBuffer(ctx, parts, viewParts, frame);
        try
        {
            var lines = PrefabLineArtRenderer.DrawLines(ctx, scene, frame);
            var rect = PrefabLineArtRenderer.ContentRect(lines, 24);
            var cropped = PrefabLineArtRenderer.Crop(lines, rect);
            var png = cropped.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(outDir, baseName + ".png"), png);
            UnityEngine.Object.DestroyImmediate(cropped);
            UnityEngine.Object.DestroyImmediate(lines);

            // 画素ごとの項目（上の行から。0=なし、k+1=項目k）
            var ids = PrefabLineArtRenderer.ReadIds(ctx, scene, frame);
            var labels = new ushort[rect.width * rect.height];
            for (int y = 0; y < rect.height; y++)
            {
                int sy = rect.y + rect.height - 1 - y;
                for (int x = 0; x < rect.width; x++)
                {
                    int id = Mathf.RoundToInt(ids[sy * frame.outWidth + rect.x + x]) - 1;
                    int label = id >= 0 ? labelOfPart(id) : -1;
                    labels[y * rect.width + x] = (ushort)(label >= 0 ? label + 1 : 0);
                }
            }
            WriteData(Path.Combine(outDir, baseName + ".js"), baseName + ".js", new Dictionary<string, object>
            {
                ["image"] = "data:image/png;base64," + Convert.ToBase64String(png),
                ["labels"] = RunLengthBase64(labels),
            });
            var result = new Dictionary<string, object>
            {
                ["image"] = baseName + ".png",
                ["data"] = baseName + ".js",
                // この図に写っている項目（隠れている部品を選んだとき、見える向きを示すのに使う）
                ["shown"] = labels.Where(l => l > 0).Select(l => l - 1).Distinct().OrderBy(l => l).ToList(),
                ["width"] = rect.width,
                ["height"] = rect.height,
            };
            if (xrayItems != null)
            {
                WriteData(Path.Combine(outDir, baseName + "_x.js"), baseName + "_x.js",
                    new Dictionary<string, object> { ["items"] = RenderXray(ctx, parts, viewParts, frame, rect, xrayItems) });
                result["xray"] = baseName + "_x.js";
            }
            return result;
        }
        finally
        {
            PrefabLineArtRenderer.FreeRT(scene);
        }
    }

    /// <summary>
    /// 部品ごとの透かし：その部品だけを同じ向き・範囲で描き、線(2)と範囲(1)を部品の外接矩形の中で [値, 個数] にする
    /// 他の部品の陰に隠れている部品も、選んだときに形と位置を示せるようにするため
    /// 部品の写る所だけを描く（図全体を部品の数だけ描くと、R8120 で 1 方向 9 分かかっていた）
    /// </summary>
    static List<object> RenderXray(PrefabLineArtRenderer.Context ctx, List<Part> parts, List<int> viewParts,
        PrefabLineArtRenderer.ViewFrame frame, RectInt rect, List<List<int>> items)
    {
        var inView = new HashSet<int>(viewParts);
        var result = new List<object>();
        for (int k = 0; k < items.Count; k++)
        {
            var own = items[k].Where(inView.Contains).ToList();
            if (own.Count == 0) continue;
            // 部品の写る範囲（余白付き）を図の切り抜き範囲に収める
            var sub = PrefabLineArtRenderer.PixelBounds(parts, own, frame, 6);
            int sx0 = Math.Max(sub.xMin, rect.xMin), sy0 = Math.Max(sub.yMin, rect.yMin);
            int sx1 = Math.Min(sub.xMax, rect.xMax), sy1 = Math.Min(sub.yMax, rect.yMax);
            if (sx1 <= sx0 || sy1 <= sy0) continue;
            sub = new RectInt(sx0, sy0, sx1 - sx0, sy1 - sy0);
            var subFrame = PrefabLineArtRenderer.SubFrame(frame, sub);
            var gb = PrefabLineArtRenderer.RenderGBuffer(ctx, parts, own, subFrame);
            var lines = PrefabLineArtRenderer.DrawLines(ctx, gb, subFrame);
            var ids = PrefabLineArtRenderer.ReadIds(ctx, gb, subFrame);
            PrefabLineArtRenderer.FreeRT(gb);
            var px = lines.GetPixelData<byte>(0);   // RGB24、下の行から

            // 描いた範囲の中で、上の行から 0=なし 1=範囲 2=線
            int w = sub.width, h = sub.height;
            var vals = new byte[w * h];
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
            {
                int sy = h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    int si = sy * w + x;
                    byte v = px[si * 3] < 160 ? (byte)2 : (ids[si] > 0.5f ? (byte)1 : (byte)0);
                    if (v == 0) continue;
                    vals[y * w + x] = v;
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            }
            UnityEngine.Object.DestroyImmediate(lines);
            if (x1 < 0) continue;
            int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
            var box = new ushort[bw * bh];
            for (int y = 0; y < bh; y++)
            {
                for (int x = 0; x < bw; x++) box[y * bw + x] = vals[(y0 + y) * w + x0 + x];
            }
            // 図の切り抜き範囲（上の行から）での位置に直す
            result.Add(new Dictionary<string, object>
            {
                ["k"] = k,
                ["x"] = sub.xMin - rect.xMin + x0,
                ["y"] = rect.yMax - sub.yMax + y0,
                ["w"] = bw, ["h"] = bh, ["d"] = RunLengthBase64(box),
            });
        }
        return result;
    }

    /// <summary>
    /// 表示用のデータを .js で書く（LineArtLoaded("名前", データ) を呼ぶだけの1行）
    /// </summary>
    static void WriteData(string path, string id, object data)
    {
        File.WriteAllText(path, "LineArtLoaded(" + JsonSerializer.Serialize(id) + "," + JsonSerializer.Serialize(data) + ");\n");
    }

    /// <summary>
    /// ushort（リトルエンディアン）の [値, 個数] の並びを base64 にする。個数は 65535 までで区切る
    /// </summary>
    static string RunLengthBase64(ushort[] values)
    {
        var mem = new MemoryStream();
        using (var w = new BinaryWriter(mem))
        {
            for (int i = 0; i < values.Length;)
            {
                int j = i;
                while (j < values.Length && values[j] == values[i] && j - i < ushort.MaxValue) j++;
                w.Write(values[i]);
                w.Write((ushort)(j - i));
                i = j;
            }
        }
        return Convert.ToBase64String(mem.ToArray());
    }

    static GameObject FindPrefab(string name)
    {
        var paths = AssetDatabase.FindAssets($"t:Prefab {name}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileNameWithoutExtension(p) == name)
            .ToList();
        if (paths.Count > 1) Debug.Log($"[LineArt] {name} が複数あるので先頭を使います: {string.Join(", ", paths)}");
        return paths.Count > 0 ? AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]) : null;
    }

    /// <summary>
    /// 描かない物：非表示設定に当たる物（KMX の RenewHiddenObjs と同じ照合。種類=カバーも含む）と、機械のアクリル等のカバー
    /// </summary>
    static HashSet<Transform> FindHidden(Transform root, List<HiddenUnit> hiddenSettings, out int resinCovers)
    {
        var hidden = new HashSet<Transform>();
        resinCovers = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string nm = t.name;
            // アクリル等のカバーは登録しなくても隠す。半透明かどうかでは決めない
            // （位置決めやガイドのアクリル板も半透明で、それは見せたい。板金のカバーも見せる）
            if (IsResinCover(t))
            {
                hidden.Add(t);
                resinCovers++;
                continue;
            }
            foreach (var m in hiddenSettings)
            {
                if (!m.isEnable) continue;
                bool match =
                    m.mode == 0 ? nm == m.name :
                    m.mode == 1 ? nm.StartsWith(m.name) :
                    m.mode == 2 ? nm.EndsWith(m.name) :
                    m.mode == 3 ? nm.Contains(m.name) :
                    false;
                if (!match) continue;
                if (string.IsNullOrEmpty(m.parent) || HasAncestorNamed(t, m.parent))
                {
                    hidden.Add(t);
                    break;
                }
            }
        }
        return hidden;
    }

    /// <summary>CAD の標準品名がカバーで、材質がアクリル・ポリカーボネート等の樹脂板か</summary>
    static bool IsResinCover(Transform t)
    {
        return Prop(t, "KSS標準品名", "標準品名").Contains("カバー") &&
            Regex.IsMatch(Prop(t, "KSS材質１", "材質１", "材質"), "アクリル|ポリカ|PET|塩ビ|PVC|透明");
    }

    // ---------- CAD のプロパティ ----------
    static readonly Dictionary<Transform, Dictionary<string, string>> propsCache = new Dictionary<Transform, Dictionary<string, string>>();

    /// <summary>そのノードの CAD のプロパティ（無ければ null）</summary>
    static Dictionary<string, string> PropsOf(Transform t)
    {
        if (!propsCache.TryGetValue(t, out var props))
        {
            var md = t.GetComponent<UnityEngine.Pixyz.UnitySDK.Components.Metadata>();
            props = md != null ? md.getProperties() : null;
            propsCache[t] = props;
        }
        return props;
    }

    /// <summary>
    /// keys の順に最初に値のあるプロパティ（半角カナ等は全角に直す）。「---」等は値なしとみなす
    /// </summary>
    static string Prop(Transform t, params string[] keys)
    {
        var props = PropsOf(t);
        if (props == null) return "";
        foreach (var key in keys)
        {
            if (!props.TryGetValue(key, out var v) || v == null) continue;
            v = Regex.Replace(v.Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();
            if (v == "" || Regex.IsMatch(v, @"^[-ー―‐]+$")) continue;
            return v;
        }
        return "";
    }

    /// <summary>
    /// CAD（SolidWorks）のツリーと同じパス：最上位アセンブリ/構成部品-n/…/部品-n
    /// Pixyz が取り込みで足す「○○.sldasm/○○/ｺﾝﾌｨｷﾞｭﾚｰｼｮﾝ名」の 3 段は除く
    /// </summary>
    static string CadPath(Transform t)
    {
        var names = new List<string>();
        for (; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        if (names.Count >= 4 && names[1].EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)) names.RemoveRange(1, 3);
        return string.Join("/", names);
    }

    /// <summary>自分か親のどれかがその名前か（KMX の GetScenePath(o).Contains と同じ）</summary>
    static bool HasAncestorNamed(Transform t, string name)
    {
        for (; t != null; t = t.parent)
        {
            if (t.name == name) return true;
        }
        return false;
    }

    /// <summary>
    /// ユニットの動く部品を決める：path の物と、children（ユニット以外）の物、children のユニットの部品
    /// </summary>
    static List<UnitEntry> ResolveUnits(List<UnitSetting> unitSettings, List<PrefabEntry> prefabs,
        Dictionary<Transform, int> partIndex, int[] partPrefab)
    {
        var rootsByName = prefabs.ToDictionary(p => p.name, p => p.root);
        Transform Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.IndexOf('/');
            string head = slash < 0 ? path : path.Substring(0, slash);
            if (!rootsByName.TryGetValue(head, out var root)) return null;
            return slash < 0 ? root : root.Find(path.Substring(slash + 1));
        }
        void AddParts(Transform t, HashSet<int> set)
        {
            if (t == null) return;
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
            {
                if (partIndex.TryGetValue(c, out int i)) set.Add(i);
            }
        }

        // 自分の物だけ
        var own = new Dictionary<string, HashSet<int>>();
        foreach (var us in unitSettings)
        {
            var set = new HashSet<int>();
            if (!us.isRoboTimeChart)
            {
                AddParts(Find(us.path), set);
                foreach (var c in us.children ?? new List<UnitChildren>())
                {
                    if (!c.isUnit) AddParts(Find(c.path), set);
                }
            }
            own[us.name] = set;
        }
        var byName = unitSettings.GroupBy(d => d.name).ToDictionary(g => g.Key, g => g.First());
        var parentOf = new Dictionary<string, string>();
        foreach (var us in unitSettings)
        {
            foreach (var c in us.children ?? new List<UnitChildren>())
            {
                if (c.isUnit && byName.ContainsKey(c.name)) parentOf[c.name] = us.name;
            }
        }
        // 子ユニットの物も含める
        HashSet<int> Collect(string name, HashSet<string> visiting)
        {
            var set = new HashSet<int>(own[name]);
            if (!visiting.Add(name)) return set;
            foreach (var c in byName[name].children ?? new List<UnitChildren>())
            {
                if (c.isUnit && byName.ContainsKey(c.name)) set.UnionWith(Collect(c.name, visiting));
            }
            visiting.Remove(name);
            return set;
        }

        var units = new List<UnitEntry>();
        foreach (var us in byName.Values)
        {
            var set = Collect(us.name, new HashSet<string>());
            if (set.Count == 0) continue;
            var entry = new UnitEntry
            {
                setting = us,
                parts = set,
                parentUnit = parentOf.TryGetValue(us.name, out var pn) ? pn : null,
                childUnits = (us.children ?? new List<UnitChildren>()).Where(c => c.isUnit).Select(c => c.name).ToList(),
            };
            // 部品が一番多いプレハブに属させる
            entry.prefab = set.GroupBy(i => partPrefab[i]).OrderByDescending(g => g.Count()).First().Key;
            units.Add(entry);
        }
        for (int u = 0; u < units.Count; u++)
        {
            prefabs[units[u].prefab].units.Add(u);
        }
        return units;
    }

    /// <summary>
    /// ユニットの中の購入部品をまとめる：メッシュごとに、近い親から順に部品名の決まりに合うノードを探す
    /// （オートスイッチのようにシリンダの下にある物は、近い方＝センサになる）
    /// </summary>
    static List<CompEntry> ResolveComponents(List<UnitEntry> units, List<Part> parts)
    {
        var infoCache = new Dictionary<Transform, LineArtComponentClassifier.Info>();
        LineArtComponentClassifier.Info InfoOf(Transform t)
        {
            if (!infoCache.TryGetValue(t, out var info))
            {
                info = LineArtComponentClassifier.Classify(t.name);
                infoCache[t] = info;
            }
            return info;
        }

        var comps = new List<CompEntry>();
        for (int u = 0; u < units.Count; u++)
        {
            var byKey = new Dictionary<string, CompEntry>();
            foreach (int i in units[u].parts.OrderBy(i => i))
            {
                LineArtComponentClassifier.Info found = null;
                var t = parts[i].source;
                for (; t != null && found == null; t = found == null ? t.parent : t)
                {
                    found = InfoOf(t);
                }
                if (found == null) continue;
                var node = t;
                // センサが本体・ナット・座金のように分かれているときは、上のセンサにまとめる
                if (found.type == "センサ")
                {
                    for (var p = t.parent; p != null; p = p.parent)
                    {
                        var up = InfoOf(p);
                        if (up == null || up.type != "センサ") break;
                        found = up;
                        node = p;
                    }
                }
                // 同じ型式でも別のサブアセンブリにある物は別の部品（機器番号と 1 対 1 にするため）。
                // 「Copy of 部分^元の部品」のように同じ親の下で分かれた物は一つにまとめる
                string key = found.key + "|" + (node.parent != null ? node.parent.GetInstanceID() : 0);
                if (!byKey.TryGetValue(key, out var comp))
                {
                    comp = new CompEntry { unit = u, info = found };
                    byKey[key] = comp;
                    units[u].comps.Add(comps.Count);
                    comps.Add(comp);
                }
                if (!comp.nodes.Contains(node)) comp.nodes.Add(node);
                comp.parts.Add(i);
            }
        }
        return comps;
    }
}
#endif
