using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KMXLauncher
{
    /// <summary>
    /// 起動中の画面：KMX のスプラッシュ画像に、光の流れる線・機番と製番名・段階・進み具合を重ねる。
    /// 段階は KMX の Player.log の行から決める（Unity は DLL を読み終わるまで自分の画面を出せないため）
    /// </summary>
    internal class SplashForm : Form
    {
        #region 見た目
        /// <summary>画面の大きさ（DPI 100% のとき）</summary>
        private static readonly Size BaseSize = new Size(768, 432);

        private static readonly Color Accent = Color.FromArgb(232, 68, 30);
        private static readonly Color Light = Color.FromArgb(226, 230, 238);
        private static readonly Color Dim = Color.FromArgb(122, 130, 148);
        private static readonly Color Faint = Color.FromArgb(78, 86, 104);
        private static readonly Color Edge = Color.FromArgb(38, 46, 62);
        private static readonly Color Track = Color.FromArgb(26, 31, 42);

        /// <summary>スプラッシュ画像の中の、オレンジの線の位置（画像の幅・高さに対する割合）</summary>
        private const float LineY = 0.5675f, LineX0 = 0.333f, LineX1 = 0.667f;

        private static readonly string[] StageTexts =
        {
            "KMX を起動しています",
            "描画の準備をしています",
            "プログラムを読み込んでいます",
            "画面を準備しています",
            "まもなく開きます",
        };
        /// <summary>段階ごとの進み具合（始め→次の段階の始めへ、時定数 tau 秒で近づく）</summary>
        private static readonly float[] StageBase = { 0.03f, 0.10f, 0.20f, 0.86f, 0.96f, 1.00f };
        private static readonly float[] StageTau = { 3f, 3f, 25f, 6f, 2f };
        #endregion

        private static Image splash;

        private readonly KmxInfo info;
        private readonly string exe;
        private readonly string[] args;
        private readonly float scale;
        private readonly Timer timer = new Timer { Interval = 16 };
        private readonly Stopwatch clock = Stopwatch.StartNew();

        private Process kmx;
        private DateTime startUtc;
        private int stage;
        private double stageStart;
        private float shown;
        private double lastPoll;
        private long logPos;
        private bool logFound;
        private double windowSeen = -1;
        private double closingStart = -1;
        private bool failed;

        public SplashForm(KmxInfo info, string exe, string[] args)
        {
            this.info = info;
            this.exe = exe;
            this.args = args ?? new string[0];
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                scale = g.DpiX / 96f;
            }
            Text = "KMX " + info.mechId;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(BaseSize.Width * scale), (int)(BaseSize.Height * scale));
            BackColor = Color.FromArgb(11, 14, 20);
            ShowInTaskbar = true;
            TopMost = true;
            Opacity = 0;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            timer.Tick += (s, e) => Tick();
        }

        /// <summary>影を付ける</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                return cp;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11 は角を丸くする（10 では何もしない）
            try
            {
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch
            {
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
            if (exe == null)
            {
                return;   // プレビュー
            }
            startUtc = DateTime.UtcNow;
            try
            {
                kmx = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = string.Join(" ", args.Select(Quote)),
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                });
            }
            catch (Exception ex)
            {
                timer.Stop();
                MessageBox.Show(this, "KMX を起動できませんでした。\n" + ex.Message, "KMX", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private static string Quote(string a)
        {
            return a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        // ドラッグで動かせるようにする
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION
            }
        }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

        private double Now => clock.Elapsed.TotalSeconds;

        private void Tick()
        {
            var now = Now;
            // ふわっと出す・消す
            if (closingStart >= 0)
            {
                var t = (now - closingStart) / 0.25;
                if (t >= 1)
                {
                    timer.Stop();
                    Close();
                    return;
                }
                Opacity = 1 - t;
            }
            else if (Opacity < 1)
            {
                Opacity = Math.Min(1, now / 0.18);
            }

            if (now - lastPoll >= 0.25 && closingStart < 0 && !failed)
            {
                lastPoll = now;
                if (exe == null)
                {
                    Preview(now);
                }
                else
                {
                    Poll(now);
                }
            }

            // 進み具合はなめらかに追いかける
            shown += (Target(now) - shown) * 0.08f;
            Invalidate();
        }

        /// <summary>今の段階の進み具合の目標</summary>
        private float Target(double now)
        {
            int s = Math.Min(stage, StageTau.Length - 1);
            var k = 1 - Math.Exp(-(now - stageStart) / StageTau[s]);
            return (float)(StageBase[s] + (StageBase[s + 1] - StageBase[s]) * k);
        }

        private void SetStage(int s, double now)
        {
            if (s > stage)
            {
                stage = s;
                stageStart = now;
            }
        }

        /// <summary>プレビュー：段階を 2.5 秒ごとに進め、最後まで行ったら閉じる</summary>
        private void Preview(double now)
        {
            SetStage(Math.Min(4, (int)(now / 2.5)), now);
            if (stage == 4 && now - stageStart > 1.5)
            {
                closingStart = now;
            }
        }

        /// <summary>
        /// KMX の様子を見る：Player.log から段階を決め、準備ができて画面が出たら閉じる。途中で終わったら知らせる
        /// </summary>
        private void Poll(double now)
        {
            ReadLog(now);
            if (kmx == null)
            {
                return;
            }
            kmx.Refresh();
            if (kmx.HasExited)
            {
                Fail(kmx.ExitCode);
                return;
            }
            var hwnd = kmx.MainWindowHandle;
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            {
                if (windowSeen < 0)
                {
                    windowSeen = now;
                }
                // 準備ができたら（ログが無い・読めないときは、画面が出てしばらくしたら）閉じる
                if (stage >= 4 || (!logFound && now - windowSeen > 4) || now - windowSeen > 120)
                {
                    TopMost = false;
                    Program.BringToFront(hwnd);
                    closingStart = now;
                }
            }
        }

        /// <summary>
        /// Player.log の増えた分を読む（前回のログを読まないよう、起動してから書かれた物だけ）
        /// </summary>
        private void ReadLog(double now)
        {
            var path = info.logPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }
            try
            {
                if (File.GetLastWriteTimeUtc(path) < startUtc.AddSeconds(-2))
                {
                    return;
                }
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (!logFound || stream.Length < logPos)
                    {
                        logPos = 0;
                    }
                    logFound = true;
                    if (stream.Length <= logPos)
                    {
                        return;
                    }
                    stream.Seek(logPos, SeekOrigin.Begin);
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    logPos = stream.Length;
                    if (text.Contains("Initialize engine")) SetStage(1, now);
                    if (text.Contains("Begin MonoManager ReloadAssembly")) SetStage(2, now);
                    if (text.Contains("Loaded All Assemblies")) SetStage(3, now);
                    if (text.Contains("UnloadTime") || text.Contains("***** Load Start")) SetStage(4, now);
                }
            }
            catch (IOException)
            {
                // 書いている最中。次に読む
            }
        }

        private void Fail(int exitCode)
        {
            failed = true;
            timer.Stop();
            TopMost = false;
            var hasLog = !string.IsNullOrEmpty(info.logPath) && File.Exists(info.logPath);
            var message = $"KMX が起動の途中で終わりました（終了コード {exitCode}）。" + (hasLog ? "\nログ（Player.log）を開きますか？" : "");
            if (MessageBox.Show(this, message, "KMX", hasLog ? MessageBoxButtons.YesNo : MessageBoxButtons.OK, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(info.logPath) { UseShellExecute = true });
                }
                catch
                {
                }
            }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var now = Now;
            Render(e.Graphics, ClientRectangle, info, stage, shown, now, now - stageStart, exe == null ? now : (DateTime.UtcNow - startUtc).TotalSeconds);
        }

        /// <summary>
        /// 確認用：画面を画像に書き出す（2 倍の大きさ）
        /// </summary>
        public static void Snapshot(KmxInfo info, string outPath, int stage)
        {
            if (string.IsNullOrEmpty(info.mechId))
            {
                info.mechId = "S1820";
                info.name = "ZOU自動電気検査装置 設計検討製番";
            }
            var size = new Size(BaseSize.Width * 2, BaseSize.Height * 2);
            using (var bmp = new Bitmap(size.Width, size.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                var progress = StageBase[stage] + (StageBase[stage + 1] - StageBase[stage]) * 0.5f;
                Render(g, new Rectangle(Point.Empty, size), info, stage, progress, 0.75, 22, 34);
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>
        /// 描く
        /// </summary>
        /// <param name="anim">動きの時刻（秒）</param>
        /// <param name="inStage">今の段階に入ってからの秒</param>
        /// <param name="elapsed">起動してからの秒</param>
        private static void Render(Graphics g, Rectangle r, KmxInfo info, int stage, float progress, double anim, double inStage, double elapsed)
        {
            float w = r.Width, h = r.Height, u = h / BaseSize.Height;   // u：1px（DPI 100%・基本の大きさ）
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            if (splash == null)
            {
                using (var s = typeof(SplashForm).Assembly.GetManifestResourceStream("splash.png"))
                {
                    splash = s != null ? Image.FromStream(s) : null;
                }
            }
            if (splash != null)
            {
                g.DrawImage(splash, r);
            }
            else
            {
                using (var b = new SolidBrush(Color.FromArgb(11, 14, 20))) g.FillRectangle(b, r);
            }

            DrawSweep(g, w, h, u, anim);

            // 左上：会社名（字間を空けて控えめに）
            DrawSpaced(g, "KYOTO SEISAKUSHO", new Font("Segoe UI", 7.5f * u, FontStyle.Regular, GraphicsUnit.Pixel), Faint, 22 * u, 20 * u, 2.6f * u);

            // 機番（オレンジ）と製番名
            using (var fMech = new Font("Segoe UI Semibold", 17f * u, GraphicsUnit.Pixel))
            using (var fName = new Font("Yu Gothic UI", 14f * u, GraphicsUnit.Pixel))
            {
                var mech = info.mechId ?? "";
                var name = info.name ?? "";
                var sf = StringFormat.GenericTypographic;
                var mw = mech == "" ? 0 : g.MeasureString(mech, fMech, PointF.Empty, sf).Width;
                var gap = mech != "" && name != "" ? 12 * u : 0;
                var maxName = w - 120 * u - mw - gap;
                name = Fit(g, name, fName, maxName, sf);
                var nw = name == "" ? 0 : g.MeasureString(name, fName, PointF.Empty, sf).Width;
                var x = (w - (mw + gap + nw)) / 2;
                var y = h * 0.745f;
                using (var b = new SolidBrush(Accent)) g.DrawString(mech, fMech, b, x, y, sf);
                using (var b = new SolidBrush(Light)) g.DrawString(name, fName, b, x + mw + gap, y + 3.5f * u, sf);
            }

            // 段階と経過時間
            using (var fStage = new Font("Yu Gothic UI", 11.5f * u, GraphicsUnit.Pixel))
            using (var fSmall = new Font("Yu Gothic UI", 10f * u, GraphicsUnit.Pixel))
            {
                var dots = new string('・', 1 + (int)(anim * 2.5) % 3).PadRight(3, '　');
                var text = StageTexts[Math.Min(stage, StageTexts.Length - 1)];
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                using (var b = new SolidBrush(Dim))
                {
                    g.DrawString($"{text} {dots}  {(int)elapsed} 秒", fStage, b, new RectangleF(0, h * 0.835f, w, 24 * u), sf);
                }
                // プログラムの読み込みが長いときの説明
                if (stage == 2 && inStage > 12)
                {
                    using (var b = new SolidBrush(Faint))
                    {
                        g.DrawString("初めての起動や、ネットワーク上のフォルダからの起動は時間がかかります", fSmall, b, new RectangleF(0, h * 0.89f, w, 20 * u), sf);
                    }
                }
            }

            // 下端の進み具合
            var trackH = 3 * u;
            using (var b = new SolidBrush(Track)) g.FillRectangle(b, 0, h - trackH, w, trackH);
            var pw = Math.Max(1, w * Math.Max(0, Math.Min(1, progress)));
            using (var b = new LinearGradientBrush(new RectangleF(0, h - trackH, pw + 1, trackH), Color.FromArgb(150, 40, 16), Color.FromArgb(255, 120, 70), LinearGradientMode.Horizontal))
            {
                g.FillRectangle(b, 0, h - trackH, pw, trackH);
            }
            using (var b = new SolidBrush(Color.FromArgb(120, 255, 170, 120)))
            {
                g.FillRectangle(b, pw - 6 * u, h - trackH, 6 * u, trackH);
            }

            // 縁
            using (var p = new Pen(Edge, 1)) g.DrawRectangle(p, 0, 0, w - 1, h - 1);
        }

        /// <summary>
        /// スプラッシュのオレンジの線の上を、光が左から右へ流れる
        /// </summary>
        private static void DrawSweep(Graphics g, float w, float h, float u, double anim)
        {
            const double period = 2.2;
            var t = (float)(anim % period / period);
            var ease = t * t * (3 - 2 * t);
            var x0 = w * LineX0;
            var x1 = w * LineX1;
            var x = x0 + (x1 - x0) * ease;
            var y = h * LineY;
            var fade = (float)Math.Sin(Math.PI * t);   // 端では消える

            // にじみ
            var haloW = w * 0.13f;
            var haloH = 18 * u;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x - haloW / 2, y - haloH / 2, haloW, haloH);
                using (var b = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb((int)(70 * fade), 255, 110, 50),
                    SurroundColors = new[] { Color.FromArgb(0, 255, 110, 50) },
                })
                {
                    g.FillPath(b, path);
                }
            }
            // 芯
            var coreW = w * 0.075f;
            var coreH = 2.2f * u;
            var rect = new RectangleF(x - coreW / 2, y - coreH / 2, coreW, coreH);
            using (var b = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                b.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(0, 255, 200, 160),
                        Color.FromArgb((int)(240 * fade), 255, 226, 200),
                        Color.FromArgb(0, 255, 200, 160),
                    },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                g.FillRectangle(b, rect);
            }
        }

        /// <summary>字間を空けて書く</summary>
        private static void DrawSpaced(Graphics g, string text, Font font, Color color, float x, float y, float spacing)
        {
            using (font)
            using (var b = new SolidBrush(color))
            {
                var sf = StringFormat.GenericTypographic;
                foreach (var c in text)
                {
                    var s = c.ToString();
                    g.DrawString(s, font, b, x, y, sf);
                    x += (c == ' ' ? font.Size * 0.35f : g.MeasureString(s, font, PointF.Empty, sf).Width) + spacing;
                }
            }
        }

        /// <summary>幅に収まらなければ末尾を「…」にする</summary>
        private static string Fit(Graphics g, string text, Font font, float max, StringFormat sf)
        {
            if (text == "" || g.MeasureString(text, font, PointF.Empty, sf).Width <= max)
            {
                return text;
            }
            for (int n = text.Length - 1; n > 0; n--)
            {
                var s = text.Substring(0, n) + "…";
                if (g.MeasureString(s, font, PointF.Empty, sf).Width <= max)
                {
                    return s;
                }
            }
            return "…";
        }
    }
}
