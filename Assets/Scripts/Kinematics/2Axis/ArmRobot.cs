using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Unity.VisualScripting.Metadata;

public class ArmRobot : Kinematics3D
{
    /// <summary>
    /// ２軸アーム用オブジェクト
    /// </summary>
    protected GameObject arm;

    /// <summary>
    /// 角度
    /// </summary>
    [SerializeField]
    protected List<float> angle;

    /// <summary>
    /// アーム長1
    /// </summary>
    protected float r1;

    /// <summary>
    /// アーム長2
    /// </summary>
    protected float r2;

    protected GameObject arm1_1;
    protected GameObject arm1_2;
    protected GameObject arm1_3;
    protected GameObject arm1Lever;
    protected GameObject armTri;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject plate;

    private Vector3 ang1_1;
    private Vector3 ang1_2;
    private Vector3 ang2_1;
    private Vector3 ang2_2;
    private Vector3 ang3;
    private Vector3 ang4;
    private Vector3 angP;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// パラメータ更新
    /// </summary>
    protected override void RenewParameter()
    {
        if (isChgPrm)
        {
            isChgPrm = false;
        }
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public override void SetTarget(float x, float y, float z)
    {
        angle = kinematics_R(x, y);
    }

    /// <summary>
    /// 逆解を解く
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    protected virtual List<float> kinematics_R(float x, float y)
    {
        var ret = new List<float>();
        return ret;
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        arm = new GameObject("ARM");
        arm.transform.parent = unitSetting.moveObject.transform;
        arm.transform.localPosition = Vector3.zero;
        arm.transform.localEulerAngles = Vector3.zero;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        // アーム1 W0334776-(第一アーム)
        var arm1_1Tmp = children.Find(d => d.name.Contains("W0334776-"));
        if (arm1_1Tmp != null)
        {
            arm1_1 = arm1_1Tmp.parent.gameObject;
        }

        // アーム1-2 W0334688-(二軸リンク)
        var arm1_2Tmp = children.Find(d => d.name.Contains("W0334688-"));
        if (arm1_2Tmp != null)
        {
            arm1_2 = arm1_2Tmp.parent.gameObject;
        }

        // 姿勢保持1 W0334703-(第一姿勢保持リンク)
        var arm1_3Tmp = children.Find(d => d.name.Contains("W0334703-"));
        if (arm1_3Tmp != null)
        {
            arm1_3 = arm1_3Tmp.parent.gameObject;
        }

        // アームレバー　W0334679-(二軸レバー)
        var arm1LeverTmp = children.Find(d => d.name.Contains("W0334679-"));
        if (arm1LeverTmp != null)
        {
            arm1Lever = arm1LeverTmp.parent.gameObject;
        }

        // 三角プレート W0334712- W0652636-(フィン)
        var armTriTmp = children.Find(d => d.name.Contains("W0334712-") || d.name.Contains("W0652636-"));
        if (armTriTmp != null)
        {
            armTri = armTriTmp.parent.gameObject;
        }

        // アーム2-1 W0334864-(第二アーム)
        var arm2_1Tmp = children.Find(d => d.name.Contains("W0334864-"));
        if (arm2_1Tmp != null)
        {
            arm2_1 = arm2_1Tmp.parent.gameObject;
        }

        // アーム2-2 W0656252-(第二姿勢保持リンク)
        var arm2_2Tmp = children.Find(d => d.name.Contains("W0656252-"));
        if (arm2_2Tmp != null)
        {
            arm2_2 = arm2_2Tmp.parent.gameObject;
        }

        // プレート W0334721-(ヘッド)
        var plateTmp = children.Find(d => d.name.Contains("W0334721-"));
        if (plateTmp != null)
        {
            plate = plateTmp.parent.gameObject;
        }

        // 親子関係セット
        arm1_1.transform.parent = arm.transform;
        arm1_3.transform.parent = arm.transform;
        arm1_2.transform.parent = arm1Lever.transform;
        arm1Lever.transform.parent = arm.transform;
        armTri.transform.parent = arm1_1.transform;
        arm2_1.transform.parent = arm1_1.transform;
        arm2_2.transform.parent = armTri.transform;
        plate.transform.parent = arm2_1.transform;
    }
}
