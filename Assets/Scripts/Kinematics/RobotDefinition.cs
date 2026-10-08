using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ロボットの定義（KMX の開発者が派生ロボットをコードを直さずに登録するためのもの）。
/// 型（動き方＝ArmRobot 等の C# クラス）ごとに1件、判別に使う名前と、アーム（役割）ごとの部品の名前の一覧を
/// Datas/Robots/RobotModels.json に書く。派生ロボットは、その型のアームの一覧に名前を足して登録する。
/// 全く新しい形のロボットは型をコードで作る
/// </summary>
[Serializable]
public class RobotDefinition
{
    /// <summary>
    /// 表示名（ログ用）
    /// </summary>
    public string name { get; set; } = "";

    /// <summary>
    /// 型（RobotType の名前。例：ARM / CEILING_ARM / MPX_R3 / CRX_30iA）
    /// </summary>
    public string type { get; set; } = "";

    /// <summary>
    /// 判別に使う名前（ユニットのモデルの子に、どれか1つでも含む名前があればこの型）
    /// </summary>
    public List<string> detect { get; set; } = new();

    /// <summary>
    /// アーム（役割）ごとの部品の名前（先に書いたものから探す）。
    /// "名前" はその名前を含む物そのもの、"parent:名前" はその親、"parent:parent:名前" は親の親
    /// </summary>
    public Dictionary<string, List<string>> arms { get; set; } = new();

    /// <summary>
    /// 型（読めない時は UNDEFINED）
    /// </summary>
    public RobotType RobotType
    {
        get
        {
            return Enum.TryParse<RobotType>(type, true, out var t) ? t : RobotType.UNDEFINED;
        }
    }

    /// <summary>
    /// このアーム（役割）の名前を定義しているか
    /// </summary>
    public bool HasArm(string role)
    {
        return (arms != null) && arms.TryGetValue(role, out var list) && (list != null) && list.Any(p => !string.IsNullOrEmpty(p));
    }
}

/// <summary>
/// ロボットの定義の一覧（KMX に同梱：Datas/Robots/RobotModels.json。上に書いた型から順に判別する）
/// </summary>
public static class RobotDefinitions
{
    /// <summary>
    /// 定義（判別する順）
    /// </summary>
    private static List<RobotDefinition> definitions = new();

    /// <summary>
    /// 定義ファイル（StreamingAssets から。KMXTool の出力では上書きされない）
    /// </summary>
    private const string FileName = "Datas/Robots/RobotModels.json";

    /// <summary>
    /// ユニットごとの判別結果（ログを毎回出さないため。読み込み直しで消す）
    /// </summary>
    private static readonly Dictionary<UnitSetting, RobotDefinition> resolved = new();

    /// <summary>
    /// 読み込む（読み込み時に1回）
    /// </summary>
    public static async Task LoadAsync()
    {
        resolved.Clear();
        definitions = await LoadFile(FileName);
        Debug.Log($"[Robot] ロボットの定義 {definitions.Count}件（{FileName}）");
    }

    private static async Task<List<RobotDefinition>> LoadFile(string file)
    {
        try
        {
            var json = await GlobalScript.LoadJsonFromStreamingAssetsAsync(file);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<RobotDefinition>();
            }
            var list = JsonSerializer.Deserialize<List<RobotDefinition>>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return list ?? new List<RobotDefinition>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Robot] {file} を読めませんでした（アームはコードの既定の名前で探します）：{ex.Message}");
            return new List<RobotDefinition>();
        }
    }

    /// <summary>
    /// 型の定義（無ければ null）
    /// </summary>
    public static RobotDefinition Get(RobotType type)
    {
        return definitions.FirstOrDefault(d => d.RobotType == type);
    }

    /// <summary>
    /// ユニットの型を決める（モデルの子の名前で判別。当たらなければ null）
    /// </summary>
    public static RobotDefinition Resolve(UnitSetting unitSetting)
    {
        if (unitSetting == null)
        {
            return null;
        }
        if (resolved.TryGetValue(unitSetting, out var cached))
        {
            return cached;
        }
        RobotDefinition result = null;
        string reason = "";
        if (unitSetting.moveObject != null)
        {
            var children = unitSetting.moveObject.GetComponentsInChildren<Transform>(true);
            foreach (var def in definitions)
            {
                if (def.detect == null)
                {
                    continue;
                }
                var hit = def.detect.FirstOrDefault(p => !string.IsNullOrEmpty(p) && children.Any(c => c.name.Contains(p)));
                if (hit != null)
                {
                    result = def;
                    reason = $"判別：{hit}";
                    break;
                }
            }
        }
        resolved[unitSetting] = result;
        if (result != null)
        {
            Debug.Log($"[Robot] {unitSetting.name}: 定義「{result.name}」（型 {result.RobotType}・{reason}）");
        }
        else
        {
            Debug.Log($"[Robot] {unitSetting.name}: ロボットの定義に当たらないため、コードの判別を使います");
        }
        return result;
    }

    /// <summary>
    /// アームの部品を探す道具を作る（型の定義のアームの名前で探し、定義に無いアームはコードの既定の名前で探す）。
    /// ロボットの中に入れた別のユニット（登録したヘッド・子ユニット）の中は探さない。
    /// （ヘッドのユニット名「N1_ヘッド」が、プレートを探す名前「ヘッド」に当たり、ヘッドのユニットをプレートとして回していた。
    ///   アーム長もそこから測るので、ヘッドを付けると位置が変わっていた）
    /// </summary>
    /// <param name="head">登録したヘッド（無ければ null）</param>
    public static RobotArmFinder Finder(UnitSetting unitSetting, RobotType type, List<Transform> children, GameObject head = null)
    {
        var excludes = new List<Transform>();
        if (head != null)
        {
            excludes.Add(head.transform);
        }
        if ((unitSetting != null) && (unitSetting.children != null))
        {
            foreach (var child in unitSetting.children)
            {
                if (child.isUnit && (child.childObject != null))
                {
                    excludes.Add(child.childObject.transform);
                }
            }
        }
        var list = (excludes.Count == 0) ? children : children.FindAll(t => !excludes.Exists(e => (t == e) || t.IsChildOf(e)));
        return new RobotArmFinder(unitSetting, type, Get(type), list);
    }
}

/// <summary>
/// アーム（役割）の部品を名前で探す。型の定義にそのアームの名前があればそれだけで探し、無ければコードの既定の名前で探す。
/// 名前の書き方は定義ファイルと同じ（"名前"＝その物、"parent:名前"＝親、"parent:parent:名前"＝親の親）
/// </summary>
public class RobotArmFinder
{
    private const string ParentPrefix = "parent:";

    private readonly UnitSetting unitSetting;
    private readonly RobotType type;
    private readonly RobotDefinition definition;
    private readonly List<Transform> children;

    /// <summary>
    /// 探した結果（ログ用。見つからなければ found は null）
    /// </summary>
    private readonly List<(string role, string found, bool fromDefinition)> results = new();

    public RobotArmFinder(UnitSetting unitSetting, RobotType type, RobotDefinition definition, List<Transform> children)
    {
        this.unitSetting = unitSetting;
        this.type = type;
        this.definition = definition;
        this.children = children;
    }

    /// <summary>
    /// アームの部品を1つ探す（名前の一覧を先頭から順に試し、最初に見つかった物）
    /// </summary>
    public GameObject Find(string role, params string[] defaults)
    {
        var fromDefinition = (definition != null) && definition.HasArm(role);
        GameObject found = null;
        foreach (var pattern in fromDefinition ? definition.arms[role] : defaults.ToList())
        {
            if (!TryParse(pattern, out var key, out var up))
            {
                continue;
            }
            found = Up(children.Find(d => d.name.Contains(key)), up);
            if (found != null)
            {
                break;
            }
        }
        results.Add((role, found != null ? found.name : null, fromDefinition));
        return found;
    }

    /// <summary>
    /// アームの部品を全部探す（名前の一覧の順に、それぞれ当たる物を全部。同じ部品が複数ある時用）
    /// </summary>
    public List<GameObject> FindAll(string role, params string[] defaults)
    {
        var fromDefinition = (definition != null) && definition.HasArm(role);
        var list = new List<GameObject>();
        foreach (var pattern in fromDefinition ? definition.arms[role] : defaults.ToList())
        {
            if (!TryParse(pattern, out var key, out var up))
            {
                continue;
            }
            foreach (var hit in children.FindAll(d => d.name.Contains(key)))
            {
                var obj = Up(hit, up);
                if ((obj != null) && !list.Contains(obj))
                {
                    list.Add(obj);
                }
            }
        }
        results.Add((role, list.Count > 0 ? $"{list.Count}個" : null, fromDefinition));
        return list;
    }

    /// <summary>
    /// 探した結果をログに出す（見つからないアームがあれば警告）
    /// </summary>
    public void Log()
    {
        var name = unitSetting != null ? unitSetting.name : "?";
        var source = definition != null ? $"定義「{definition.name}」" : "コードの既定";
        var text = string.Join(" / ", results.Select(r => $"{r.role}={r.found ?? "なし"}{(r.fromDefinition ? "" : "(既定)")}"));
        var missing = results.Where(r => r.found == null).Select(r => r.role).ToList();
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[Robot] {name}（{type}・{source}）: {text}　※見つからない：{string.Join("・", missing)}");
        }
        else
        {
            Debug.Log($"[Robot] {name}（{type}・{source}）: {text}");
        }
    }

    /// <summary>
    /// 名前の書き方を読む（先頭の "parent:" の数だけ親をたどる）
    /// </summary>
    private static bool TryParse(string pattern, out string key, out int up)
    {
        key = pattern ?? "";
        up = 0;
        while (key.StartsWith(ParentPrefix))
        {
            key = key.Substring(ParentPrefix.Length);
            up++;
        }
        return !string.IsNullOrEmpty(key);
    }

    /// <summary>
    /// 親を up 回たどる（たどれなければ null）
    /// </summary>
    private static GameObject Up(Transform t, int up)
    {
        for (var i = 0; (i < up) && (t != null); i++)
        {
            t = t.parent;
        }
        return t != null ? t.gameObject : null;
    }
}
