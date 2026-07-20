using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// KMX の関節経路(度) を FANUC 汎用再生用の CSV へ変換する（kmx_ros2/FANUC_CSV_PLAY_SPEC.md 方式A）。
///
/// 経路ごとに .LS プログラムを生成するのではなく、FANUC側の**固定・汎用再生プログラム**（Karel が CSV を読み
/// `PR[]`(joint) に充填 → TP が `J PR[i] % CNT` で再生）に渡す**データだけ**を出す。
///
/// フォーマット（FANUC_CSV_PLAY_SPEC.md §2・確定）: コメント行なし・CRLF・カンマ区切り。
///   1行目 ヘッダ: count,group,uframe,utool,speed,cnt        （整数6）
///   2行目～ 点:   J1,J2,J3,J4,J5,J6                          （度・小数3桁・J2-J3換算済み）
///
/// - 関節角で渡す（IK曖昧さ回避・計画した衝突フリー経路を忠実に）。
/// - J3 は FANUC 規約へ換算（出力J3 = 入力J3 − J2。<see cref="FanucLsExporter"/> の j2j3Coupling と同一）。
/// - 点数は `$MAXPREGNUM`(既定100)以下に**間引き**（Phase1・一括ロード）。無制限は Phase2 のバッチ再生。
/// </summary>
public static class FanucCsvExporter
{
    public sealed class Options
    {
        public int speedPercent = 100;  // J 移動速度(%)。1-100
        public int cnt = 50;             // CNT値(0-100)。0=FINE相当。滑らかさ vs 経路忠実
        public int uframe = 0;           // UF
        public int utool = 1;            // UT
        public int group = 1;            // 動作グループ GP
        public bool j2j3Coupling = true; // FANUC J2-J3 連動: 出力J3 = 入力J3 - J2
        public int maxPoints = 100;      // ≤ $MAXPREGNUM(既定100) に間引く。0以下=間引かない
    }

    private const string NL = "\r\n";    // FANUC ASCII は CRLF

    /// <summary>関節経路(点ごとに度[jointCount]) から CSV 文字列を生成する。</summary>
    public static string Build(IReadOnlyList<double[]> jointsDegPerPoint, Options opt = null)
    {
        opt = opt ?? new Options();
        if (jointsDegPerPoint == null || jointsDegPerPoint.Count == 0)
        {
            throw new ArgumentException("経路点が空です");
        }
        var pts = Downsample(jointsDegPerPoint, opt.maxPoints);
        int n = pts.Count;
        int speed = Math.Max(1, Math.Min(100, opt.speedPercent));
        int cnt = Math.Max(0, Math.Min(100, opt.cnt));
        var ci = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        // ---- ヘッダ（整数6）----
        sb.Append(n.ToString(ci)).Append(',')
          .Append(opt.group.ToString(ci)).Append(',')
          .Append(opt.uframe.ToString(ci)).Append(',')
          .Append(opt.utool.ToString(ci)).Append(',')
          .Append(speed.ToString(ci)).Append(',')
          .Append(cnt.ToString(ci)).Append(NL);

        // ---- 点（J1..J6・度・固定6列）----
        for (int i = 0; i < n; i++)
        {
            var j = pts[i];
            if (j == null)
            {
                throw new ArgumentException($"点{i + 1} の関節値が空です");
            }
            for (int k = 0; k < 6; k++)
            {
                // FANUC J2-J3 連動: 3軸目(J3)は ROS純粋角から J2 を差し引いて TP規約へ。範囲外は0。
                double val;
                if (opt.j2j3Coupling && k == 2 && j.Length >= 3) { val = j[2] - j[1]; }
                else if (k < j.Length) { val = j[k]; }
                else { val = 0.0; }
                sb.Append(val.ToString("F3", ci));
                sb.Append(k < 5 ? "," : "");
            }
            sb.Append(NL);
        }
        return sb.ToString();
    }

    /// <summary>点数が max を超える場合、端点を保持して等間隔に間引く（max以下ならそのまま）。</summary>
    private static List<double[]> Downsample(IReadOnlyList<double[]> src, int max)
    {
        int n = src.Count;
        if (max <= 0 || n <= max)
        {
            var all = new List<double[]>(n);
            for (int i = 0; i < n; i++) { all.Add(src[i]); }
            return all;
        }
        var outp = new List<double[]>(max);
        for (int i = 0; i < max; i++)
        {
            // i∈[0,max-1] を [0,n-1] へ写像（端点保持）。
            int idx = (int)Math.Round((double)i * (n - 1) / (max - 1));
            if (idx < 0) { idx = 0; }
            if (idx > n - 1) { idx = n - 1; }
            outp.Add(src[idx]);
        }
        return outp;
    }
}
