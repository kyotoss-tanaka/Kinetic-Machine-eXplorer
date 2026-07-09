/// <summary>
/// 機種ごとの「定格関節速度(°/s)」テーブル。経路の各軸ピーク角速度と比較して定格比/超過を判定する。
///
/// ★★★ ここの値は全て暫定（プレースホルダ）です。★★★
///   CRX-30iA / RS007L の各軸最大速度（データシートの最大関節速度）が判明したら、必ず実値へ差し替えること。
///   後で ROS2(URDF/joint_limits) から取得する方式へ置き換えても良い。
/// </summary>
public static class Ros2MotorLimits
{
    /// <summary>機種キー(ModelKey)の各軸 定格角速度(°/s)。未知機種は既定(暫定)。</summary>
    public static float[] MaxJointSpeedDeg(string modelKey, int jointCount)
    {
        if (modelKey == "crx30ia")
        {
            // ★暫定：FANUC 公式データシート(PDF)から機械抽出できず。典型的な CRX 値を仮置き。
            //   要データシート確認で差し替え（協働ロボで速度制限あり）。
            return new float[] { 120f, 120f, 120f, 180f, 180f, 180f };
        }
        if (modelKey == "rs007l")
        {
            // KAWASAKI 公式スペック（kawasakirobotics.com RS007L 仕様書）より。
            //   JT1:370 JT2:310 JT3:410 JT4:550 JT5:550 JT6:1000 (°/s)
            return new float[] { 370f, 310f, 410f, 550f, 550f, 1000f };
        }
        int n = jointCount > 0 ? jointCount : 6;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            d[i] = 180f;   // 既定（暫定）
        }
        return d;
    }
}
