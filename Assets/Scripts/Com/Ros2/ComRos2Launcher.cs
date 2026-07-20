using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// Unity から WSL 上の ROS2 bringup を起動 / 停止 / 再起動するランチャ（方式A＝wsl.exe を叩く）。
///
/// - 制御スクリプトは ROS2 側が提供（`~/ros2_ws/kmx_{start,stop,restart,status}.sh`）。Unity は呼ぶだけ。
///   仕様: `kmx_ros2/LAUNCH_CONTROL_UNITY_SPEC.md`。
/// - endpoint 自体が bringup の一部なので、コールドスタートは ROS 経由でなく Windows プロセス起動で行う。
/// - `kmx_status.sh` を定期ポーリングし State(stopped/starting/running_full) を公開。UI はこれでランプ表示。
/// - wsl.exe 実行は別スレッド（UIを止めない）。Unity API はメインスレッドのみで触る。
/// - Windows(エディタ/スタンドアロン)専用。それ以外では自身を無効化。
///
/// 設定は `Ros2Info.json`(ComRos2.Ros2Setting) の wslUser / wslDistro / launchUseMoveit を参照。
/// </summary>
[DisallowMultipleComponent]
public sealed class ComRos2Launcher : MonoBehaviour
{
    public enum LaunchState { Unknown, Stopped, Starting, RunningFull }

    [SerializeField] private string wslUser = "kyotoss";
    [SerializeField] private string wslDistro = "";
    [SerializeField] private bool useMoveit = true;
    [SerializeField] private bool launchRviz = false;   // Ros2Info.json launchRviz。KMX_RVIZ 経由で launch に渡す
    [SerializeField] private bool useMock = true;       // Ros2Info.json launchUseMock。true=模擬 / false=実機・ROBOGUIDE
    [SerializeField] private string robotIp = "192.168.1.100";  // Ros2Info.json robotIp。useMock=false 時の Stream Motion 接続先
    [SerializeField] private float statusPollSec = 1.5f;

    [SerializeField] private string robotModel = "crx30ia";   // 既定の robot_model（機種未指定で起動する場合）

    /// <summary>最新の bringup 状態（メインスレッドで更新）。</summary>
    public LaunchState State { get; private set; } = LaunchState.Unknown;
    /// <summary>ROS で現在稼働中の robot_model（`~/ros2_ws/.kmx_robot_model`）。ポーリングで更新。空=不明。</summary>
    public string CurrentRobotModel { get; private set; } = "";
    /// <summary>ROS で現在の dcs_host（`~/ros2_ws/.kmx_dcs_host`。ROS側で 127.0.0.1/localhost は "auto" に正規化済）。空=不明。</summary>
    public string CurrentDcsHost { get; private set; } = "";
    /// <summary>start/stop/restart を実行中（多重起動抑止＆UIの押下抑止用）。</summary>
    public bool Busy => busy;
    /// <summary>直近スクリプトの stderr（あれば）。</summary>
    public string LastError { get; private set; } = "";
    /// <summary>State が変化したとき通知。</summary>
    public event Action<LaunchState> StateChanged;

    private volatile bool busy;
    private volatile bool polling;
    private volatile string polledStatus;    // ワーカーが書き、メイン Update が読む
    private volatile string polledModel;     // 稼働中 robot_model（.kmx_robot_model）。ワーカーが書き、メインが読む
    private volatile string polledDcsHost;   // 稼働中 dcs_host（.kmx_dcs_host）
    private volatile float nextPoll;          // 次にポーリングする unscaledTime（0=即時）
    private bool platformOk;

    private string WsDir => $"/home/{wslUser}/ros2_ws";

    private void Start()
    {
        // wsl.exe は Windows 専用。エディタ/スタンドアロンの Windows でのみ動作。
        platformOk = Application.platform == RuntimePlatform.WindowsEditor
                  || Application.platform == RuntimePlatform.WindowsPlayer;
        if (!platformOk)
        {
            enabled = false;
            return;
        }
        LoadConfig();
        nextPoll = 0f;   // 起動直後に一度状態を取りにいく
    }

    /// <summary>Ros2Info.json から WSL ユーザー/ディストロ/MoveIt 既定を読む（無ければ既定値のまま）。</summary>
    private void LoadConfig()
    {
        try
        {
            var cfg = GlobalScript.LoadJson<ComRos2.Ros2Setting>("Ros2Info") as ComRos2.Ros2Setting;
            if (cfg != null)
            {
                if (!string.IsNullOrEmpty(cfg.wslUser)) { wslUser = cfg.wslUser; }
                wslDistro = cfg.wslDistro ?? "";
                useMoveit = cfg.launchUseMoveit;
                launchRviz = cfg.launchRviz;
                useMock = cfg.launchUseMock;
                if (!string.IsNullOrEmpty(cfg.robotIp)) { robotIp = cfg.robotIp; }
            }
        }
        catch { /* 無ければ既定値 */ }
    }

    private void Update()
    {
        if (!platformOk)
        {
            return;
        }
        // ワーカーが取得した状態をメインスレッドで反映。
        var s = polledStatus;
        if (s != null)
        {
            polledStatus = null;
            ApplyStatus(s);
            var m = polledModel;
            if (m != null) { CurrentRobotModel = m; }   // 稼働中モデルも反映（空=不明）
            var d = polledDcsHost;
            if (d != null) { CurrentDcsHost = d; }       // 稼働中 dcs_host も反映
        }
        // 定期ポーリング（実行中でなければ）。
        if (!polling && Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + Mathf.Max(0.5f, statusPollSec);
            PollStatusAsync();
        }
    }

    private void ApplyStatus(string raw)
    {
        LaunchState ns = raw switch
        {
            "running_full" => LaunchState.RunningFull,
            "starting" => LaunchState.Starting,
            "stopped" => LaunchState.Stopped,
            _ => LaunchState.Unknown,
        };
        if (ns != State)
        {
            State = ns;
            StateChanged?.Invoke(State);
        }
    }

    // ── 操作（UI から呼ぶ） ─────────────────────────────
    // 引数順 kmx_start.sh: <use_moveit> <rviz> <robot_model> <use_mock> <robot_ip> <dcs_host>（ROBOT_SWITCH_UNITY_SPEC.md §1）。
    //   ・use_mock は CSV運用では常に true（Stream Motion 未使用）。robot_ip($5) も未使用。
    //   ・dcs_host($6) に機体ごとの RobotInfo.json robotIp を渡す（ROBOGUIDE=127.0.0.1→ROS側で auto 読替／実機=コントローラIP）。
    public void StartRos2(string model, string dcsHost)
        => RunScriptAsync($"{WsDir}/kmx_start.sh {(useMoveit ? "true" : "false")} {(launchRviz ? "1" : "0")} {SafeArg(model, robotModel)} {(useMock ? "true" : "false")} {SafeArg(robotIp, "127.0.0.1")} {SafeArg(dcsHost, "auto")}");

    /// <summary>既定 robot_model / dcs_host=auto で起動（機種未指定時）。</summary>
    public void StartRos2() => StartRos2(robotModel, "auto");

    public void StopRos2()
        => RunScriptAsync($"{WsDir}/kmx_stop.sh");

    public void RestartRos2(string model, string dcsHost)
        => RunScriptAsync($"{WsDir}/kmx_restart.sh {(useMoveit ? "true" : "false")} {(launchRviz ? "1" : "0")} {SafeArg(model, robotModel)} {(useMock ? "true" : "false")} {SafeArg(robotIp, "127.0.0.1")} {SafeArg(dcsHost, "auto")}");

    public void RestartRos2() => RestartRos2(robotModel, "auto");

    /// <summary>bash -lc の引用符内に入る引数を検証（空白/メタ文字を含む値は fallback＝コマンド破壊・注入防止）。</summary>
    private static string SafeArg(string s, string fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) { return fallback; }
        s = s.Trim();
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == ';' || c == '$' || c == '`' || c == '&' || c == '|' || c == '<' || c == '>')
            {
                return fallback;
            }
        }
        return s;
    }

    // ── 内部：非同期実行 ─────────────────────────────
    private void RunScriptAsync(string scriptCmd)
    {
        if (!platformOk || busy)
        {
            return;
        }
        busy = true;
        LastError = "";
        StartWorker(() =>
        {
            // start は running_full まで待って return する（最大 ~45s）。余裕をもって待つ。
            string outp = RunWslBlocking(scriptCmd, out string err, out int exit, 60000);
            if (!string.IsNullOrEmpty(err)) { LastError = err.Trim(); }
            Debug.Log($"[ComRos2Launcher] 実行完了: exit={exit}\n  cmd: wsl.exe -e bash -lc \"{scriptCmd}\"\n  out: {outp?.Trim()}\n  err: {err?.Trim()}");
            if (exit != 0 || !string.IsNullOrEmpty(err))
            {
                Debug.LogWarning($"[ComRos2Launcher] スクリプトが異常終了/警告を出しました。exit={exit} err={err?.Trim()}");
            }
            busy = false;
            nextPoll = 0f;   // 実行直後にすぐ状態を取り直す（Update が拾う）
        });
    }

    private void PollStatusAsync()
    {
        polling = true;
        StartWorker(() =>
        {
            // status / 稼働中 robot_model / dcs_host を1回の wsl 呼び出しで取得（出力: "running_full|crx30ia|auto"）。
            // 二重引用符を含めないよう区切りは echo -n '|'（bash -lc の "" 内なので ' はそのまま使える）。
            string outp = RunWslBlocking(
                $"{WsDir}/kmx_status.sh; echo -n '|'; cat {WsDir}/.kmx_robot_model 2>/dev/null; echo -n '|'; cat {WsDir}/.kmx_dcs_host 2>/dev/null",
                out _, out _, 8000);
            string st = "unknown", md = "", dh = "";
            if (!string.IsNullOrEmpty(outp))
            {
                string[] parts = outp.Split('|');
                if (parts.Length > 0) { st = parts[0].Trim(); }
                if (parts.Length > 1) { md = parts[1].Trim(); }
                if (parts.Length > 2) { dh = parts[2].Trim(); }
            }
            polledStatus = string.IsNullOrEmpty(st) ? "unknown" : st;
            polledModel = md;
            polledDcsHost = dh;
            polling = false;
        });
    }

    private static void StartWorker(Action work)
    {
        var t = new Thread(() =>
        {
            try { work(); }
            catch { /* ワーカー内例外はここで握り潰す（メインを巻き込まない） */ }
        })
        { IsBackground = true };
        t.Start();
    }

    /// <summary>wsl.exe を同期実行して stdout を返す。ワーカースレッドからのみ呼ぶ（Unity API 不使用）。</summary>
    private string RunWslBlocking(string scriptCmd, out string stderr, out int exitCode, int timeoutMs)
    {
        stderr = "";
        exitCode = -1;
        try
        {
            string distroArg = string.IsNullOrEmpty(wslDistro) ? "" : $"-d {wslDistro} ";
            // 実行ユーザーを明示（-u）。既定ディストロ/既定ユーザーが対象と異なる環境でも、
            // 対象ユーザーの ~/ros2_ws・~/.bashrc(ROS環境) を確実に使う（インストーラの -u と挙動を合わせる）。
            // 例: 配布先に Ubuntu-24.04(既定) や docker-desktop があっても、wslDistro/wslUser で狙い撃つ。
            string userArg = string.IsNullOrEmpty(wslUser) ? "" : $"-u {wslUser} ";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wsl.exe",
                // -l(ログインシェル) で ~/.bashrc 等の ROS 環境を確実に読む。
                Arguments = $"{distroArg}{userArg}-e bash -lc \"{scriptCmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,   // WSL 出力は UTF-8
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null)
            {
                stderr = "wsl.exe を起動できませんでした";
                return "";
            }
            // ReadToEnd は stdout EOF(スクリプト終了)までブロック。start は running_full まで待つ設計。
            string outp = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            if (p.WaitForExit(timeoutMs))
            {
                exitCode = p.ExitCode;
            }
            else
            {
                stderr = $"タイムアウト({timeoutMs}ms)。" + stderr;
            }
            return outp;
        }
        catch (Exception e)
        {
            stderr = e.Message;
            return "";
        }
    }
}
