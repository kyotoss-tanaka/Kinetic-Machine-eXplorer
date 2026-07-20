using System.Collections.Generic;
using System.Threading.Tasks;
using Parameters;

/// <summary>
/// StreamingAssets/Datas/SafetyZoneInfo.json から DCS安全ゾーンを読む（Phase1・フォールバック）。
/// kmx_ros2/DCS_ZONE_IMPORT_SPEC.md §4.1 / DCS_ZONE_ROS2_LIVE_SPEC.md §6。
/// ファイルが無い/壊れている場合は例外を投げるので、呼び出し側(ParameterLoader.FetchSafetyZonesAsync)が catch する。
/// </summary>
public sealed class JsonSafetyZoneSource : ISafetyZoneSource
{
    public async Task<List<SafetyZoneSetting>> FetchAsync()
    {
        var sz = await GlobalScript.LoadListJson<List<SafetyZoneSetting>>("SafetyZoneInfo");
        return (List<SafetyZoneSetting>)sz;
    }
}
