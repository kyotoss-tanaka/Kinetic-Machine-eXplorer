using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class Kinematics6D : Kinematics3D, IRos2PlanTarget
{
    #region プロパティ
    [SerializeField]
    protected TagInfo RX;

    [SerializeField]
    protected TagInfo RY;

    [SerializeField]
    protected TagInfo RZ;

    [SerializeField]
    protected Vector3 rotate;

    [SerializeField]
    protected GameObject HeadObject;

    #endregion プロパティ

    #region 変数
    protected float trxMax = 0;
    protected float trxMin = 0;
    protected float tryMax = 0;
    protected float tryMin = 0;
    protected float trzMax = 0;
    protected float trzMin = 0;
    #endregion 変数

    #region 関数

    // Start is called before the first frame update
    protected override void Start()
    {
        if (baseObject == null)
        {
            ModelRestruct();
        }
    }

    protected override void MyFixedUpdate()
    {
        if (isManual)
        {
            setTarget(target, rotate);
        }
        else
        {
            if (robo.tags.Count >= 6)
            {
                var x = GetTagValueF(robo.tags[0], ref X);
                var y = GetTagValueF(robo.tags[1], ref Y);
                var z = GetTagValueF(robo.tags[2], ref Z);
                var rx = GetTagValueF(robo.tags[3], ref RX);
                var ry = GetTagValueF(robo.tags[4], ref RY);
                var rz = GetTagValueF(robo.tags[5], ref RZ);
                target.x = CheckRangeF(x / (robo.rates[0] == 0 ? 1000f : robo.rates[0]), txMin, txMax);
                target.y = CheckRangeF(y / (robo.rates[1] == 0 ? 1000f : robo.rates[1]), tyMin, tyMax);
                target.z = CheckRangeF(z / (robo.rates[2] == 0 ? 1000f : robo.rates[2]), tzMin, tzMax);
                rotate.x = CheckRangeF(rx / (robo.rates[3] == 0 ? 1000f : robo.rates[3]), trxMin, trxMax);
                rotate.y = CheckRangeF(ry / (robo.rates[4] == 0 ? 1000f : robo.rates[4]), tryMin, tryMax);
                rotate.z = CheckRangeF(rz / (robo.rates[5] == 0 ? 1000f : robo.rates[5]), trzMin, trzMax);
                setTarget(target, rotate);
            }
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { X, Y, Z, RX, RY, RZ };
    }

    /// <summary>
    /// ヘッド(ツール)オブジェクト。6軸目(arm6)の子として付く。
    /// ROS2 連携でツール形状を planning scene へ attach するために取得する。
    /// </summary>
    public GameObject GetHeadObject()
    {
        return HeadObject;
    }

    // --- IRos2PlanTarget（機種非依存の計画対象契約） ---
    /// <summary>ユニット名（UnitSetting.name）。無ければ GameObject 名。</summary>
    public string UnitName => (unitSetting != null && !string.IsNullOrEmpty(unitSetting.name)) ? unitSetting.name : name;

    /// <summary>機種キー（robot_id 自動生成・機種別既定の索引）。サブクラスで上書き。既定は "robot"。</summary>
    public virtual string ModelKey => "robot";

    private static readonly string[] DefaultJointNames = { "J1", "J2", "J3", "J4", "J5", "J6" };
    /// <summary>関節名（機種の既定。サブクラスで上書き可）。既定は J1..J6。</summary>
    public virtual string[] JointNames => DefaultJointNames;
    /// <summary>関節数。</summary>
    public int JointCount => JointNames.Length;

    /// <summary>経路計画ステップ列（RobotInfo.json robotSteps）。robo 未設定なら null。</summary>
    public IReadOnlyList<Ros2RobotStep> PlanSteps => robo != null ? robo.robotSteps : null;

    /// <summary>現在の関節角(度)。既定はゼロ（arm 逆算はサブクラスで実装）。</summary>
    public virtual double[] GetCurrentJointsDeg() => new double[JointCount];

    /// <summary>ロボット基準(arm チェーンのルート) Transform。既定は null（サブクラスで実装）。</summary>
    public virtual Transform GetBaseTransform() => null;

    /// <summary>現在姿勢のボディコライダー（他ロボを障害物として送る用）。プレビュー用ゴースト複製は除外。</summary>
    public virtual IReadOnlyList<Collider> GetBodyColliders()
    {
        var b = GetBaseTransform();
        if (b == null)
        {
            return System.Array.Empty<Collider>();
        }
        var list = new List<Collider>();
        foreach (var c in b.GetComponentsInChildren<Collider>(true))
        {
            bool underGhost = false;
            for (var p = c.transform; p != null; p = p.parent)
            {
                if (p.name.IndexOf("_Ghost", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    underGhost = true;
                    break;
                }
            }
            if (!underGhost)
            {
                list.Add(c);
            }
        }
        return list;
    }

    /// <summary>
    /// 与えた関節角セット列(度・J1..J6順)での「先端(ツール/フランジ)」世界位置列を返す（経路プレビュー用）。
    /// 実装(サブクラス)は現在姿勢を保存→各点で pose→先端位置を記録→復元する（1コール内で完結＝表示は乱れない）。
    /// 既定は何もしない（先端不明）。
    /// </summary>
    public virtual void SampleTipWorld(IReadOnlyList<double[]> jointsDeg, List<Vector3> outWorld)
    {
        outWorld.Clear();
    }

    /// <summary>手動モードか（true=上位指令を無視して target/rotate で表示）。</summary>
    public bool IsManual => isManual;

    /// <summary>手動モードの ON/OFF（ゴール姿勢を画面で作るときに ON）。</summary>
    public void SetManual(bool on)
    {
        isManual = on;
    }

    /// <summary>手動姿勢を J1..J6(度) で設定（isManual 時に表示へ反映）。rates=1 前提の素の度。</summary>
    public void SetManualJointsDeg(double[] j)
    {
        if (j == null || j.Length < 6)
        {
            return;
        }
        target = new Vector3((float)j[0], (float)j[1], (float)j[2]);
        rotate = new Vector3((float)j[3], (float)j[4], (float)j[5]);
    }

    // --- 経路プレビュー用ゴースト（半透明複製。実機モデルは動かさず複製だけ動かす） ---
    /// <summary>ロボットの半透明複製(ゴースト)を生成して返す。既定は未対応(null)。サブクラスで実装。</summary>
    public virtual GameObject CreateGhost()
    {
        return null;
    }

    /// <summary>ゴーストを J1..J6(度) の姿勢にする。</summary>
    public virtual void PoseGhostDeg(double[] j16)
    {
    }

    /// <summary>ゴーストを破棄する。</summary>
    public virtual void DestroyGhost()
    {
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="target"></param>
    public virtual void setTarget(Vector3 targe, Vector3 rotate)
    {
        SetTarget(target.x, target.y, target.z, rotate.x, rotate.y, rotate.z);
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="rx"></param>
    /// <param name="ry"></param>
    /// <param name="rz"></param>
    public virtual void SetTarget(float x, float y, float z, float rx, float ry, float rz)
    {
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected override void SetCollision()
    {
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        if (robo.headUnit != null)
        {
            HeadObject = robo.headUnit.unitObject;
        }
    }
    #endregion 関数
}
