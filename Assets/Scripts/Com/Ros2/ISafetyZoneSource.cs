using System.Collections.Generic;
using System.Threading.Tasks;
using Parameters;

/// <summary>
/// DCS安全ゾーンの取得元（ROS / JSON）。kmx_ros2/DCS_ZONE_ROS2_LIVE_SPEC.md §6。
/// ParameterLoader が「ROS優先→未対応/失敗なら JSON」の順で問い合わせる。
/// </summary>
public interface ISafetyZoneSource
{
    /// <summary>
    /// ゾーン一覧を取得（非同期）。取得できない/未対応なら null を返す（呼び出し側が次のソースへフォールバック）。
    /// 返す値は素の DCS 値（mm・robot World/base フレーム）。mm→m・軸写像・原点合わせは SafetyZoneScript 側で行う。
    /// </summary>
    Task<List<SafetyZoneSetting>> FetchAsync();
}
