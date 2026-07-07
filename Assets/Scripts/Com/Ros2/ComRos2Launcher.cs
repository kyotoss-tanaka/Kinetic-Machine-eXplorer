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
    [SerializeField] private float statusPollSec = 1.5f;

    /// <summary>最新の bringup 状態（メインスレッドで更新）。</summary>
    public LaunchState State { get; private set; } = LaunchState.Unknown;
    /// <summary>start/stop/restart を実行中（多重起動抑止＆UIの押下抑止用）。</summary>
    public bool Busy => busy;
    /// <summary>直近スクリプトの stderr（あれば）。</summary>
    public string LastError { get; private set; } = "";
    /// <summary>State が変化したとき通知。</summary>
    public event Action<LaunchState> StateChanged;

    private volatile bool busy;
    private volatile bool polling;
    private volatile string polledStatus;    // ワーカーが書き、メイン Update が読む
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
    public void StartRos2()
        => RunScriptAsync($"{WsDir}/kmx_start.sh {(useMoveit ? "true" : "false")}");

    public void StopRos2()
        => RunScriptAsync($"{WsDir}/kmx_stop.sh");

    public void RestartRos2()
        => RunScriptAsync($"{WsDir}/kmx_restart.sh {(useMoveit ? "true" : "false")}");

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
            string outp = RunWslBlocking($"{WsDir}/kmx_status.sh", out _, out _, 8000);
            polledStatus = string.IsNullOrEmpty(outp) ? "unknown" : outp.Trim();
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
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "wsl.exe",
                // -l(ログインシェル) で ~/.bashrc 等の ROS 環境を確実に読む。
                Arguments = $"{distroArg}-e bash -lc \"{scriptCmd}\"",
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
