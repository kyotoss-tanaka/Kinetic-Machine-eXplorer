using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// KMX の関節経路(度) を FANUC TP プログラム(.LS ASCII) へ変換する（オフライン方式・Stream Motion 不要）。
///
/// - 各点を「関節移動 J P[i] &lt;speed&gt;% &lt;term&gt;」で並べ、位置は関節表現(J1..Jn deg)で出力する。
///   関節値をそのまま使うので IK 曖昧さが無く、計画した衝突フリー経路を崩さない。
/// - &lt;term&gt; は FINE 既定（各点で位置決め＝経路に忠実）。CNT は角を丸めて経路から外れる（衝突リスク）ので
///   使うなら小さめ＋密な点で。
/// - 生成した .LS を ROBOGUIDE / コントローラに取り込んで再生する。
///
/// ※ FANUC の ASCII(.LS) 翻訳は書式に厳しい。最初の取り込みで書式エラーが出たら、その内容に合わせて微調整する。
///   設計背景はメモリ [[fanuc-recovery-motion]]。
/// </summary>
public static class FanucLsExporter
{
    public sealed class Options
    {
        public int speedPercent = 100;  // J 移動速度(%)
        public bool fine = false;        // true=FINE(各点停止・忠実だが遅い) / false=CNT(連続・速い。密点なら経路逸脱は小)
        public int cnt = 100;            // fine=false 時の CNT 値(0..100)。大きいほど滑らか＆速いが角を丸める
        public int uframe = 0;           // ユーザフレーム UF
        public int utool = 1;            // ツールフレーム UT
        public int group = 1;            // 動作グループ GP
        public bool j2j3Coupling = true; // FANUC J2-J3 連動: 出力J3 = 入力J3 - J2（ROSの純粋関節角→FANUC TP規約へ）
        public string comment = "KMX path";
    }

    private const string NL = "\r\n";    // FANUC ASCII は CRLF

    /// <summary>関節経路(点ごとに度[jointCount]) から .LS 文字列を生成する。</summary>
    public static string Build(string progName, IReadOnlyList<double[]> jointsDegPerPoint, Options opt = null)
    {
        opt = opt ?? new Options();
        if (jointsDegPerPoint == null || jointsDegPerPoint.Count == 0)
        {
            throw new ArgumentException("経路点が空です");
        }
        string name = SanitizeName(progName);
        int n = jointsDegPerPoint.Count;
        int speed = Math.Max(1, Math.Min(100, opt.speedPercent));
        string term = opt.fine ? "FINE" : ("CNT" + Math.Max(0, Math.Min(100, opt.cnt)));
        var ci = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        // ---- ヘッダ / 属性 ----
        sb.Append("/PROG  ").Append(name).Append(NL);
        sb.Append("/ATTR").Append(NL);
        sb.Append("OWNER\t\t= MNEDITOR;").Append(NL);
        sb.Append("COMMENT\t\t= \"").Append(Clip(opt.comment, 16)).Append("\";").Append(NL);
        sb.Append("PROG_SIZE\t= 0;").Append(NL);
        sb.Append("CREATE\t\t= DATE 00-00-00  TIME 00:00:00;").Append(NL);
        sb.Append("MODIFIED\t= DATE 00-00-00  TIME 00:00:00;").Append(NL);
        sb.Append("FILE_NAME\t= ;").Append(NL);
        sb.Append("VERSION\t\t= 0;").Append(NL);
        sb.Append("LINE_COUNT\t= ").Append((n + 2).ToString(ci)).Append(";").Append(NL);   // +2 = UFRAME_NUM/UTOOL_NUM 行
        sb.Append("MEMORY_SIZE\t= 0;").Append(NL);
        sb.Append("PROTECT\t\t= READ_WRITE;").Append(NL);
        sb.Append("TCD:  STACK_SIZE\t= 0,").Append(NL);
        sb.Append("      TASK_PRIORITY\t= 50,").Append(NL);
        sb.Append("      TIME_SLICE\t= 0,").Append(NL);
        sb.Append("      BUSY_LAMP_OFF\t= 0,").Append(NL);
        sb.Append("      ABORT_REQUEST\t= 0,").Append(NL);
        sb.Append("      PAUSE_REQUEST\t= 0;").Append(NL);
        sb.Append("DEFAULT_GROUP\t= ").Append(opt.group.ToString(ci)).Append(",*,*,*,*;").Append(NL);
        sb.Append("CONTROL_CODE\t= 00000000 00000000;").Append(NL);
        sb.Append("/APPL").Append(NL);

        // ---- 動作行（各点へ関節移動） ----
        // 冒頭で UFRAME_NUM/UTOOL_NUM を位置データ(UF/UT)と同じ番号に設定する。
        //   ＝プログラム自身がアクティブ座標系番号を自分の位置に合わせるので、どのコントローラ/状態でも
        //     「実行-251 UTが表示データと一致しません」警告が出ず、移植性が高い（ROBOGUIDE 実地検証で判明）。
        sb.Append("/MN").Append(NL);
        int lineNo = 0;
        sb.Append("  ").Append((++lineNo).ToString(ci)).Append(":  UFRAME_NUM=").Append(opt.uframe.ToString(ci)).Append("    ;").Append(NL);
        sb.Append("  ").Append((++lineNo).ToString(ci)).Append(":  UTOOL_NUM=").Append(opt.utool.ToString(ci)).Append("    ;").Append(NL);
        for (int i = 1; i <= n; i++)
        {
            sb.Append("  ").Append((++lineNo).ToString(ci)).Append(":J P[").Append(i.ToString(ci))
              .Append("] ").Append(speed.ToString(ci)).Append("% ").Append(term).Append("    ;").Append(NL);
        }

        // ---- 位置（関節表現 J1..Jn deg・3軸ずつ） ----
        sb.Append("/POS").Append(NL);
        for (int i = 1; i <= n; i++)
        {
            var j = jointsDegPerPoint[i - 1];
            if (j == null || j.Length == 0)
            {
                throw new ArgumentException($"点{i} の関節値が空です");
            }
            sb.Append("P[").Append(i.ToString(ci)).Append("]{").Append(NL);
            sb.Append("   GP").Append(opt.group.ToString(ci)).Append(":").Append(NL);
            sb.Append("\tUF : ").Append(opt.uframe.ToString(ci)).Append(", UT : ").Append(opt.utool.ToString(ci)).Append(",").Append(NL);
            for (int k = 0; k < j.Length; k++)
            {
                bool lineStart = (k % 3 == 0);
                bool last = (k == j.Length - 1);
                bool lineEnd = (k % 3 == 2) || last;
                // FANUC J2-J3 連動: 3軸目(J3)は ROS純粋角から J2 を差し引いて TP規約へ変換。
                double val = (opt.j2j3Coupling && k == 2 && j.Length >= 3) ? (j[2] - j[1]) : j[k];
                if (lineStart) { sb.Append("\t"); }
                sb.Append("J").Append((k + 1).ToString(ci)).Append("=").Append(val.ToString("F3", ci).PadLeft(10)).Append(" deg");
                if (!last) { sb.Append(","); }
                sb.Append(lineEnd ? NL : "\t");
            }
            sb.Append("};").Append(NL);
        }

        sb.Append("/END").Append(NL);
        return sb.ToString();
    }

    /// <summary>プログラム名を FANUC の規則へ（英数と_・先頭英字・大文字・36文字以内）。</summary>
    private static string SanitizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) { return "KMXPATH"; }
        var sb = new StringBuilder();
        foreach (char c in s.ToUpperInvariant())
        {
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') { sb.Append(c); }
        }
        string r = sb.ToString();
        if (r.Length == 0) { r = "KMXPATH"; }
        if (!(r[0] >= 'A' && r[0] <= 'Z')) { r = "P" + r; }   // 先頭は英字
        if (r.Length > 36) { r = r.Substring(0, 36); }
        return r;
    }

    private static string Clip(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) { return ""; }
        s = s.Replace("\"", "'");
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
