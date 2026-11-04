#if UNITY_EDITOR
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// CAD のノード名から購入部品（シリンダ・センサ・モータ等）を見分ける
///
/// 部品表の命名の決まり：「項番(2桁) + 図番コード(英字+数字) _ 型式 _ メーカー - インスタンス番号」
///   例) 01L2_MY1B25-500LZ-M9P-6_SMC-1 / 91M7_HG-KR23B_ﾐﾂﾋﾞｼﾃﾞﾝｷ-1
/// 加工で分けた物は「Copy of 部分^元の部品名」になるので、^ の後ろ（元の部品）でも判定する
/// 種類はメーカーと型式の頭で決める（当てはまらなければ「その他」）
/// </summary>
public static class LineArtComponentClassifier
{
    public class Info
    {
        /// <summary>同じ部品を一つにまとめる名前</summary>
        public string key;
        /// <summary>型式</summary>
        public string partNo;
        /// <summary>メーカー（全角に直したもの）</summary>
        public string maker;
        /// <summary>種類</summary>
        public string type;
        /// <summary>項番（2桁）</summary>
        public string item;
        /// <summary>図番コード（L2 等）</summary>
        public string code;
    }

    /// <summary>種類の並び（Web ページの一覧の順）</summary>
    public static readonly string[] Types =
    {
        "シリンダ", "チャック", "ロータリ", "バルブ", "センサ", "真空・圧力", "モータ", "アンプ", "減速機",
        "ガイド・ボールねじ", "軸受", "継手・配管", "ケーブル", "締結", "その他",
    };

    static readonly Regex Purchased = new Regex(@"^(\d{2})([A-Z]\d)_(.+)_([^_]+?)(-\d+)?$");
    static readonly Regex InHouseWithMaker = new Regex(@"^R\d{4}-([A-Z]\d)-(.+)_([^_]+?)(-\d+)?$");
    static readonly Regex InHouseKnownPart = new Regex(@"^R\d{4}-([A-Z]\d)-((?:HG-|HF-|HK-|MR-J|EE-SX|E3Z|E2E)[^(（_]*)");
    // 型式の途中（MHF2-12D-M9PL の「D-M9」等）では拾わないよう、頭か区切りの直後だけ
    static readonly Regex SensorPart = new Regex(@"(?<![A-Z0-9])(D-M9\w*|D-A9\w*|D-F\d\w*|EE-SX\w*|E3Z[\w-]*|E2E[\w-]*|E3S[\w-]*|E3T[\w-]*|ZSE\w*|ISE\w*|PSE\w*)");

    static readonly string[] KnownMakers =
    {
        "SMC", "THK", "NTN", "NSK", "NMB", "NBK", "IKO", "KHK", "NOK", "ASK", "RITTAL",
        "ミツビシデンキ", "ミスミ", "サンコウキコウ", "イワタ", "イマオ", "ツバキモトチェーン", "ニホンデンサンシンポ",
        "イグス", "フジセイミツ", "ソテック", "NICオートテック", "オムロン", "キーエンス", "パナソニック", "オリエンタルモーター",
        "CKD", "コガネイ", "ピスコ",
    };

    /// <summary>
    /// ノード名から部品を見分ける（部品でなければ null）
    /// </summary>
    public static Info Classify(string rawName)
    {
        string name = rawName;
        while (name.StartsWith("Copy of "))
        {
            name = name.Substring("Copy of ".Length);
        }
        string piece = name, origin = null;
        int caret = name.IndexOf('^');
        if (caret >= 0)
        {
            piece = name.Substring(0, caret);
            origin = name.Substring(caret + 1);
        }

        // シリンダに付いたオートスイッチ等は、シリンダとは別のセンサとして扱う
        var sensor = SensorPart.Match(Normalize(piece));
        if (sensor.Success && !Purchased.IsMatch(piece))
        {
            return new Info { key = piece, partNo = sensor.Value, maker = "", type = "センサ", item = "", code = "" };
        }
        return FromName(piece) ?? (origin != null ? FromName(origin) : null);
    }

    static Info FromName(string name)
    {
        var m = Purchased.Match(name);
        if (m.Success)
        {
            return Make(name, m.Groups[3].Value, m.Groups[4].Value, m.Groups[1].Value, m.Groups[2].Value);
        }
        m = InHouseWithMaker.Match(name);
        if (m.Success && KnownMakers.Contains(Normalize(m.Groups[3].Value)))
        {
            return Make(name, m.Groups[2].Value, m.Groups[3].Value, "", m.Groups[1].Value);
        }
        m = InHouseKnownPart.Match(name);
        if (m.Success)
        {
            return Make(name, m.Groups[2].Value.Trim(), "", "", m.Groups[1].Value);
        }
        return null;
    }

    static Info Make(string key, string partNo, string maker, string item, string code)
    {
        var info = new Info
        {
            key = key,
            partNo = Normalize(partNo),
            maker = Normalize(maker),
            item = item,
            code = code,
        };
        info.type = TypeOf(info.maker, info.partNo.ToUpperInvariant());
        return info;
    }

    /// <summary>半角カナ等を全角に直す</summary>
    static string Normalize(string s)
    {
        return s.Normalize(NormalizationForm.FormKC);
    }

    static bool Starts(string p, params string[] heads)
    {
        foreach (var h in heads)
        {
            if (p.StartsWith(h)) return true;
        }
        return false;
    }

    /// <summary>
    /// 種類をメーカーと型式の頭で決める（順番に意味がある：先に当たった物を使う）
    /// </summary>
    static string TypeOf(string maker, string p)
    {
        if (maker == "サンコウキコウ" || maker == "フジセイミツ" ||
            Starts(p, "CS-", "SS-", "MSTP", "UN-", "HN-", "SW-", "WS-", "PW-", "CBM", "SCB", "HXN", "FWSSU", "NUT"))
            return "締結";
        if (SensorPart.IsMatch(p) || maker == "オムロン" || maker == "キーエンス" || maker == "パナソニック" ||
            Starts(p, "FU-", "LR-", "PZ-", "GT2", "IL-", "E2S"))
            return "センサ";
        if (p.Contains("CBL") || Starts(p, "SC-PWS", "SC-", "MR-PWS", "MR-BKS", "MR-EKCBL", "MR-J3ENCBL"))
            return "ケーブル";
        if (Starts(p, "MR-J", "MR-JE", "BLE2D", "AZD", "ARD", "SGD"))
            return "アンプ";
        if (Starts(p, "HG-", "HF-", "HK-", "HJ-", "HC-", "BLM", "BLE", "BXM", "5IK", "4IK", "AZM", "ARM", "PKP", "SGM", "MSM") ||
            maker == "オリエンタルモーター" || p.Contains("モータ"))
            return "モータ";
        if (maker == "ニホンデンサンシンポ" || Starts(p, "VRGF", "VRSF", "VRL", "VRB", "EVL"))
            return "減速機";
        if (Starts(p, "ZP", "ZH", "ZFC", "ZK2", "ZQ", "ZX"))
            return "真空・圧力";
        if (maker == "SMC" || maker == "CKD" || maker == "コガネイ" || maker == "ピスコ")
        {
            if (Starts(p, "MH")) return "チャック";
            if (Starts(p, "CRB", "CDRB", "MSQ", "CRQ", "CDRA", "CRA")) return "ロータリ";
            if (Starts(p, "SY", "SJ", "VQ", "VP", "VHS", "VX", "VT", "VV", "SS5", "EX", "ITV", "SV")) return "バルブ";
            if (Starts(p, "KQ", "KJ", "KK", "KM", "KF", "KX", "KS", "KN", "M-", "AS", "AW", "AN", "AF", "AR", "AL", "TU", "TS", "VHK", "IDF", "AMD", "ZFB"))
                return "継手・配管";
            if (Starts(p, "C", "MG", "MX", "MY", "MK", "MU", "MB", "MD", "RE", "ML"))
                return "シリンダ";
            return "その他";
        }
        if (maker == "THK" || Starts(p, "SRS", "SHS", "HSR", "SSR", "KSF", "BNK", "BIF", "SXR", "MGN", "LWL", "LWE", "LV"))
            return "ガイド・ボールねじ";
        if (maker == "NTN" || maker == "NSK" || maker == "NMB" || maker == "IKO" ||
            Regex.IsMatch(p, @"^(\d{4}|6\d{3}|FLW|BB\d|NA\d|BRW|BGR|FL6)"))
            return "軸受";
        return "その他";
    }
}
#endif
