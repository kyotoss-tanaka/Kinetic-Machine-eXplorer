using System;
using System.Collections.Generic;
using UnityEngine;
using Parameters;

/// <summary>
/// シーン内の「経路計画可能なロボット」(IRos2PlanTarget・6軸以上)を列挙し、選択状態を保持する。
/// 各ロボは ComRos2 の RobotConfigs(Ros2Info の robots) と unit 名で突合する。
/// パネル(セレクタ)/計画(ComRos2PathPlanner)/障害物(ComRos2Obstacles) が参照。
/// robots[] 空・未登録でも、シーン内の IRos2PlanTarget を発見して既定で登録する（後方互換）。
/// </summary>
[DisallowMultipleComponent]
public sealed class Ros2PlanTargetRegistry : MonoBehaviour
{
    /// <summary>登録された1ロボット（計画対象＋その設定）。</summary>
    public sealed class RegisteredRobot
    {
        public IRos2PlanTarget Target;
        public ComRos2.Ros2RobotConfig Config;   // null 可（Ros2Info 未登録＝既定合成）
        public string RobotId => (Config != null && !string.IsNullOrEmpty(Config.robotId)) ? Config.robotId : "";
        public string DisplayName => Target != null ? Target.UnitName : "?";
        /// <summary>この機体の関節名（設定優先・無ければ機種既定）。</summary>
        public string[] JointNames =>
            (Config != null && Config.jointNames != null && Config.jointNames.Length > 0)
                ? Config.jointNames : (Target != null ? Target.JointNames : null);
    }

    private readonly List<RegisteredRobot> robots = new();
    private int selectedIndex = -1;
    private bool built;

    public IReadOnlyList<RegisteredRobot> Robots => robots;
    public RegisteredRobot Selected =>
        (selectedIndex >= 0 && selectedIndex < robots.Count) ? robots[selectedIndex] : null;
    public int SelectedIndex => selectedIndex;
    public bool IsBuilt => built;

    /// <summary>構築完了/選択変更の通知。パネルはこれで再描画・リターゲットする。</summary>
    public event Action Changed;

    private void Update()
    {
        // ロード完了後に一度だけ列挙（unitSettings/kinematics が揃ってから）。
        if (!built && GlobalScript.isLoaded)
        {
            Build(GetComponent<ComRos2>());
        }
    }

    /// <summary>計画可能ロボットを列挙する（ロード完了後）。</summary>
    public void Build(ComRos2 com)
    {
        robots.Clear();
        selectedIndex = -1;
        var configs = com != null ? com.RobotConfigs : null;
        var units = GlobalScript.unitSettings;
        var modelCount = new Dictionary<string, int>();   // 機種別の通番（robot_id 自動生成用）
        if (units != null)
        {
            foreach (var u in units)
            {
                if (u == null || u.robotSetting == null || u.moveObject == null)
                {
                    continue;
                }
                var target = u.moveObject.GetComponent<IRos2PlanTarget>();
                if (target == null || target.JointCount < 6)
                {
                    continue;   // 経路生成対象は 6 軸以上の IRos2PlanTarget のみ
                }
                // Ros2Info robots[] に明示があれば優先。無ければ機種キー＋既定から自動合成（Ros2Info を書かなくても動く）。
                var cfg = FindConfig(configs, u) ?? SynthesizeConfig(target, u, modelCount);
                robots.Add(new RegisteredRobot { Target = target, Config = cfg });
            }
        }
        if (robots.Count > 0)
        {
            selectedIndex = 0;
        }
        built = true;
        Debug.Log($"[Ros2PlanTargetRegistry] 計画可能ロボット {robots.Count}台: "
            + string.Join(", ", robots.ConvertAll(r => $"{r.DisplayName}(id={r.RobotId},joints={r.JointNames?.Length ?? 0})")));
        Changed?.Invoke();
    }

    private static ComRos2.Ros2RobotConfig FindConfig(IReadOnlyList<ComRos2.Ros2RobotConfig> configs, UnitSetting u)
    {
        if (configs == null)
        {
            return null;
        }
        foreach (var c in configs)
        {
            if (c != null
                && string.Equals(c.unit, u.name, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(c.mechId) || c.mechId == u.mechId))
            {
                return c;
            }
        }
        return null;
    }

    /// <summary>
    /// Ros2Info robots[] に無いロボの設定を、機種キー(ModelKey)と機種別既定から自動生成する。
    /// robot_id は「機種キー＋通番」(例 crx30ia_1)＝ROS2 robot_map と一致させる規約。
    /// base/flange は名前検索せず GetBaseTransform()/ヘッドの親を使うため空でよい。
    /// </summary>
    private static ComRos2.Ros2RobotConfig SynthesizeConfig(IRos2PlanTarget target, UnitSetting u, Dictionary<string, int> modelCount)
    {
        string model = string.IsNullOrEmpty(target.ModelKey) ? "robot" : target.ModelKey;
        int idx = (modelCount.TryGetValue(model, out var n) ? n : 0) + 1;
        modelCount[model] = idx;
        var d = ModelDefaults(model);
        return new ComRos2.Ros2RobotConfig
        {
            robotId = $"{model}_{idx}",       // 機種＋通番（例 crx30ia_1）
            unit = u.name,
            mechId = u.mechId,
            jointNames = target.JointNames,
            jointCount = target.JointCount,
            baseNameContains = "",            // GetBaseTransform() を使うので不要
            flangeNameContains = d.flange,    // ヘッド attach 用フランジ名（機種別）
            attachLinkName = d.attach,
            baseCalibrationEuler = d.calib,
        };
    }

    /// <summary>機種別の ROS2 既定（フランジ名・attach リンク名・base 補正 euler）。Ros2Info robots[] で個別に上書き可。</summary>
    private static (string flange, string attach, Vector3 calib) ModelDefaults(string modelKey)
    {
        switch (modelKey)
        {
            case "crx30ia": return ("J6FLANGE", "flange", new Vector3(0f, -90f, 0f));   // FANUC CRX-30iA（従来の CRX 既定）
            case "rs007l":  return ("_J6", "tool0", Vector3.zero);                      // KAWASAKI RS007L（実機確認後に補正調整）
            default:        return ("", "flange", Vector3.zero);
        }
    }

    /// <summary>選択を変更する（範囲外/同一は無視）。</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= robots.Count || index == selectedIndex)
        {
            return;
        }
        selectedIndex = index;
        Changed?.Invoke();
    }
}
