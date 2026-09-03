using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Parameters;
using UnityEngine;
#if KMX_ROS2
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Kmx;
#endif

/// <summary>
/// ROS経由で DCS安全ゾーンを受信する（kmx_ros2/DCS_ZONE_ROS2_LIVE_SPEC.md §6 / DCS_ZONE_ROS_REQUEST.md）。
///
/// latched topic <c>/kmx/safety_zones</c> を購読しキャッシュ。受信のたびキャッシュ更新し、内容が変われば
/// <see cref="ZonesUpdated"/> を発火して自動再描画させる。<c>FetchAsync()</c> は **接続がある限り**キャッシュを即返す
/// （＝再配信(poll)が無くても箱は消えない）。**ROS接続が切れたら**消去する（<see cref="Tick"/> が検知）。
///
/// R1（service <c>/kmx/get_safety_zones</c>）は endpoint が中継しない残課題のため未使用（DCS_ZONE_ROS_REQUEST.md §6）。
/// 取得できなければ null＝呼び出し側は空扱い＝ゾーン消去（JSONフォールバックは廃止）。
/// 受信は素の DCS 値（mm・World/base）。mm→m・軸写像・arm1原点は SafetyZoneScript 側。
///
/// ★前提: 「Robotics &gt; Generate ROS Messages」で kmx_msgs の C# 生成済み（KMX_ROS2 定義時のみコンパイル）。
/// </summary>
public sealed class RosSafetyZoneSource : ISafetyZoneSource
{
    /// <summary>topic で DCS内容が変化 or 接続が切れたとき発火（ParameterLoader が購読して自動再適用/消去）。</summary>
    public event Action ZonesUpdated;

    /// <summary>
    /// リロード(F5)時に呼ぶ。latched topic /kmx/safety_zones は「同一内容」だと再配信しても素通りする
    /// （署名一致で ZonesUpdated 非発火）ため、F5 で ROS を張り直すと一度消えた DCS が戻らない。
    /// 署名・接続キャッシュ・購読フラグを消し、再接続後の(再)配信で**必ず再購読・再発火・再適用**させる。
    /// 受信済みキャッシュ(latestFromTopic)は残すので、接続が生きていれば即再適用できる。
    /// </summary>
    public void ResetForReload()
    {
#if KMX_ROS2
        subscribed = false;
        lastSig = null;
        lastConnected = false;
        cachedRos = null;
#endif
    }

    /// <summary>毎フレーム呼ぶ（ParameterLoader.Update）。ROS接続が切れたら消去を促す（再配信の有無では消さない）。</summary>
    public void Tick()
    {
#if KMX_ROS2
        bool connected = IsConnectedNow();
        if (lastConnected && !connected)
        {
            Debug.Log("[RosSafetyZoneSource] ROS切断検知 → ゾーン消去");
            ZonesUpdated?.Invoke();   // → 再適用で FetchAsync が null（未接続）を返す→消去
        }
        lastConnected = connected;
#endif
    }

#if KMX_ROS2
    private const string TopicName = "/kmx/safety_zones";
    private bool subscribed;
    private bool lastConnected;
    private SafetyZonesMsg latestFromTopic;
    private string lastSig;
    private ROSConnection cachedRos;

    /// <summary>未発見時の再検索時刻（FindObjectsByTypeは全シーン走査で重いため毎フレーム呼ばない）</summary>
    private float nextFindTime;

    /// <summary>既存の ROSConnection を取得（無ければ null）。GetOrCreateInstance では作らない（勝手接続防止）。</summary>
    private ROSConnection GetRos()
    {
        if (cachedRos == null)
        {
            // 大規模シーンではFindObjectsByTypeが1フレーム1ms超かかるため、未発見時は5秒間隔でのみ再検索する
            if (UnityEngine.Time.unscaledTime < nextFindTime)
            {
                return null;
            }
            nextFindTime = UnityEngine.Time.unscaledTime + 5f;
            var found = UnityEngine.Object.FindObjectsByType<ROSConnection>(FindObjectsSortMode.None);
            if (found != null && found.Length > 0) { cachedRos = found[0]; }
        }
        return cachedRos;
    }

    private bool IsConnectedNow()
    {
        var ros = GetRos();
        return ros != null && ros.HasConnectionThread && !ros.HasConnectionError;
    }

    public Task<List<SafetyZoneSetting>> FetchAsync()
    {
        var ros = GetRos();
        if (ros == null)
        {
            Debug.Log("[RosSafetyZoneSource] ROSConnection 未検出（ROS未セットアップ）→ ゾーン消去");
            return Task.FromResult<List<SafetyZoneSetting>>(null);
        }
        // ROSConnection があれば接続確立前でも購読登録（接続後に有効化され自動受信できる）。
        if (!subscribed)
        {
            try { ros.Subscribe<SafetyZonesMsg>(TopicName, OnTopic); subscribed = true; Debug.Log($"[RosSafetyZoneSource] {TopicName} 購読開始"); }
            catch (Exception e) { Debug.LogWarning("[RosSafetyZoneSource] 購読失敗: " + e.Message); }
        }
        if (!ros.HasConnectionThread || ros.HasConnectionError)
        {
            Debug.Log("[RosSafetyZoneSource] ROS未接続（endpoint未接続 or 接続エラー）→ ゾーン消去");
            return Task.FromResult<List<SafetyZoneSetting>>(null);
        }

        // 接続がある限り、最後に受けた値を保持して返す（再配信が無くても消えない）。
        var cached = latestFromTopic;
        if (cached != null && cached.zones != null)
        {
            LogReceived("topic", cached);
            return Task.FromResult(Convert(cached));
        }
        Debug.Log("[RosSafetyZoneSource] topic未受信 → まだ空（初回配信待ち）");
        return Task.FromResult<List<SafetyZoneSetting>>(null);
    }

    /// <summary>topic 受信コールバック（メインスレッド）。内容変化時のみ再描画通知。</summary>
    private void OnTopic(SafetyZonesMsg msg)
    {
        latestFromTopic = msg;
        string sig = Signature(msg);
        if (sig != lastSig)
        {
            lastSig = sig;
            ZonesUpdated?.Invoke();   // → ParameterLoader が再適用（自動反映）
        }
    }

    private static string Signature(SafetyZonesMsg m)
    {
        if (m == null || m.zones == null) { return "null"; }
        var sb = new StringBuilder();
        sb.Append(m.robot_id).Append('|').Append(m.frame).Append('|').Append(m.unit).Append('|');
        foreach (var z in m.zones)
        {
            sb.Append(z.id).Append(',').Append(z.enabled).Append(',').Append(z.inside_allowed).Append(',')
              .Append(string.Join(";", z.min_mm)).Append(',').Append(string.Join(";", z.max_mm)).Append('|');
        }
        return sb.ToString();
    }

    private static void LogReceived(string via, SafetyZonesMsg z)
    {
        var z0 = (z.zones.Length > 0) ? z.zones[0] : null;
        Debug.Log($"[RosSafetyZoneSource] 受信({via}) zones={z.zones.Length} robot_id='{z.robot_id}' frame='{z.frame}'"
                  + (z0 != null ? $"  first[{z0.id}] min_mm=[{string.Join(",", z0.min_mm)}] max_mm=[{string.Join(",", z0.max_mm)}]" : ""));
    }

    /// <summary>受信 SafetyZonesMsg(mm) → Parameters.SafetyZoneSetting/SafetyZone（値はそのまま・変換はKMX側）。</summary>
    private static List<SafetyZoneSetting> Convert(SafetyZonesMsg msg)
    {
        var setting = new SafetyZoneSetting
        {
            robotId = msg.robot_id,
            // 単機(robot_id="")は name を空にして KMX側で「DCS対応ロボ(6軸+base)」へ直接結線（レジストリ非依存）。
            // 複数機(robot_id 指定)のみレジストリで unit名 を解決。[[dcs-zone-multi-robot]]
            name = string.IsNullOrEmpty(msg.robot_id) ? null : ResolveUnitName(msg.robot_id),
            frame = msg.frame,
            unit = string.IsNullOrEmpty(msg.unit) ? "mm" : msg.unit,
            zones = new List<SafetyZone>(),
        };
        foreach (var z in msg.zones)
        {
            setting.zones.Add(new SafetyZone
            {
                id = z.id,
                enabled = z.enabled,
                insideAllowed = z.inside_allowed,
                min = ToList(z.min_mm),
                max = ToList(z.max_mm),
            });
        }
        return new List<SafetyZoneSetting> { setting };
    }

    private static List<float> ToList(double[] v)
    {
        if (v == null || v.Length < 3) { return new List<float> { 0f, 0f, 0f }; }
        return new List<float> { (float)v[0], (float)v[1], (float)v[2] };
    }

    /// <summary>robot_id → ユニット名（複数機用）。未構築/不一致は null。</summary>
    private static string ResolveUnitName(string robotId)
    {
        var regs = UnityEngine.Object.FindObjectsByType<Ros2PlanTargetRegistry>(FindObjectsSortMode.None);
        if (regs == null || regs.Length == 0) { return null; }
        var reg = regs[0];
        int cnt = (reg.Robots != null) ? reg.Robots.Count : 0;
        if (cnt == 0) { return null; }
        if (!string.IsNullOrEmpty(robotId))
        {
            foreach (var r in reg.Robots)
            {
                if (r.RobotId == robotId) { return r.DisplayName; }
            }
        }
        if (cnt == 1) { return reg.Robots[0].DisplayName; }   // 単機
        return null;
    }
#else
    public Task<List<SafetyZoneSetting>> FetchAsync()
    {
        Debug.Log("[RosSafetyZoneSource] KMX_ROS2 未定義（ROS受信は無効・ROSコード非コンパイル）→ ゾーン消去");
        return Task.FromResult<List<SafetyZoneSetting>>(null);
    }
#endif
}
