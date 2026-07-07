using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ROS2 経路計画の対象となるロボット（機種非依存の契約）。Kinematics6D 等が実装する。
/// レジストリ / 計画(ComRos2PathPlanner) / 障害物(ComRos2Obstacles) / パネル は
/// この契約経由で機種に依らず操作する（将来の機種追加はこの実装を足すだけ）。
/// </summary>
public interface IRos2PlanTarget
{
    /// <summary>ユニット名（UnitSetting.name）。レジストリのキー＆Ros2Info robots との突合キー。</summary>
    string UnitName { get; }

    /// <summary>関節名（順序付き・長さ=JointCount。例 ["J1".."J6"]）。機種の既定値。</summary>
    string[] JointNames { get; }

    /// <summary>関節数（経路生成対象は 6 以上）。</summary>
    int JointCount { get; }

    /// <summary>現在の関節角(度)を返す（arm transform から逆算）。null は返さない。</summary>
    double[] GetCurrentJointsDeg();

    /// <summary>手動姿勢を関節角(度)で設定（ゴール姿勢を画面で作るとき）。</summary>
    void SetManualJointsDeg(double[] deg);

    /// <summary>手動モード ON/OFF。</summary>
    void SetManual(bool on);

    /// <summary>関節角セット列(度)での先端(ツール/フランジ)世界位置列を返す（プレビュー線用）。</summary>
    void SampleTipWorld(IReadOnlyList<double[]> jointsDeg, List<Vector3> outWorld);

    /// <summary>半透明ゴースト複製を生成して返す（未対応なら null）。</summary>
    GameObject CreateGhost();

    /// <summary>ゴーストを関節角(度)姿勢にする。</summary>
    void PoseGhostDeg(double[] deg);

    /// <summary>ゴーストを破棄する。</summary>
    void DestroyGhost();

    /// <summary>ヘッド(ツール)オブジェクト（AttachedCollisionObject 用）。</summary>
    GameObject GetHeadObject();

    /// <summary>ロボット基準（arm チェーンのルート）Transform。障害物の base フレーム。未確定なら null。</summary>
    Transform GetBaseTransform();

    /// <summary>現在姿勢のボディコライダー（このロボを「他ロボ＝障害物」として送る用）。</summary>
    IReadOnlyList<Collider> GetBodyColliders();
}
