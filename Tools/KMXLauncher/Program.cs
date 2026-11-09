using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KMXLauncher
{
    /// <summary>
    /// KMX の起動用ランチャー：押してすぐに起動中の画面を出し、横の KMX.exe を起動する。
    /// KMX の画面の準備ができたら閉じる。KMX が途中で終わったら、ログ（Player.log）を開けるようにする。
    /// 引数はそのまま KMX.exe へ渡す。ただし次の物はランチャー用（確認のため）：
    ///   --kmx-preview          KMX を起動せずに、画面の動きだけを見る（段階を順に進める）
    ///   --kmx-snapshot 出力.png 段階(0-3)   画面を画像に書き出して終わる
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var exe = Path.Combine(dir, "KMX.exe");
            var info = KmxInfo.Read(dir);

            int snap = Array.IndexOf(args, "--kmx-snapshot");
            if (snap >= 0 && snap + 2 < args.Length)
            {
                SplashForm.Snapshot(info, args[snap + 1], int.Parse(args[snap + 2]));
                return 0;
            }
            if (args.Contains("--kmx-preview"))
            {
                Application.Run(new SplashForm(info, null, null));
                return 0;
            }

            if (!File.Exists(exe))
            {
                MessageBox.Show("KMX.exe が見つかりません。\nKMXLauncher.exe は KMX.exe と同じフォルダに置いてください。\n" + dir,
                    "KMX", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            // 同じ KMX が起動していれば、それを前に出すだけにする（二重に起動しない）
            var running = FindRunning(exe);
            if (running != null)
            {
                BringToFront(running.MainWindowHandle);
                return 0;
            }
            Application.Run(new SplashForm(info, exe, args));
            return 0;
        }

        /// <summary>
        /// 同じ場所の KMX.exe が起動していればそのプロセス
        /// </summary>
        private static Process FindRunning(string exe)
        {
            foreach (var p in Process.GetProcessesByName("KMX"))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase) &&
                        p.MainWindowHandle != IntPtr.Zero)
                    {
                        return p;
                    }
                }
                catch
                {
                    // 権限の無いプロセス等
                }
            }
            return null;
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        /// <summary>
        /// ウィンドウを前に出す（最小化していれば元に戻す）
        /// </summary>
        public static void BringToFront(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, 9);   // SW_RESTORE
            }
            SetForegroundWindow(hwnd);
        }
    }

    /// <summary>
    /// 実行環境の情報（KMX_Data\app.info：1 行目が会社名、2 行目が製品名「KMX 機番_製番名」）と Player.log の場所
    /// </summary>
    internal class KmxInfo
    {
        public string mechId = "";
        public string name = "";
        public string logPath = "";

        public static KmxInfo Read(string dir)
        {
            var info = new KmxInfo();
            try
            {
                var lines = File.ReadAllLines(Path.Combine(dir, "KMX_Data", "app.info"));
                var company = lines.Length > 0 ? lines[0].Trim() : "";
                var product = lines.Length > 1 ? lines[1].Trim() : "";
                var title = product.StartsWith("KMX ") ? product.Substring(4) : product;
                var sep = title.IndexOf('_');
                info.mechId = sep > 0 ? title.Substring(0, sep) : title;
                info.name = sep > 0 ? title.Substring(sep + 1) : "";
                info.logPath = FindLog(company, product);
            }
            catch
            {
                // app.info が無い（プレビュー等）
            }
            return info;
        }

        /// <summary>
        /// Player.log の場所：%USERPROFILE%\AppData\LocalLow\会社名\製品名\Player.log
        /// （フォルダ名は、ファイル名に使えない文字と末尾の「.」「 」を「_」にした物。見つからなければ製品名のフォルダを探す）
        /// </summary>
        private static string FindLog(string company, string product)
        {
            var localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
            var path = Path.Combine(localLow, Sanitize(company), Sanitize(product), "Player.log");
            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                return path;
            }
            try
            {
                var found = Directory.GetDirectories(localLow)
                    .Select(d => Path.Combine(d, Sanitize(product)))
                    .FirstOrDefault(Directory.Exists);
                if (found != null)
                {
                    return Path.Combine(found, "Player.log");
                }
            }
            catch
            {
            }
            // 初めての起動ではまだ無い。できる場所を返しておく
            return path;
        }

        private static string Sanitize(string name)
        {
            var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
            for (int i = chars.Length - 1; i >= 0 && (chars[i] == '.' || chars[i] == ' '); i--)
            {
                chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
