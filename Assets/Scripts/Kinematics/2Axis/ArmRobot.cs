using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ArmRobot : UseHeadBase3DScript
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
    protected float L1;

    /// <summary>
    /// アーム長2
    /// </summary>
    protected float L2;

    protected GameObject arm1_1;
    protected GameObject arm1_2;
    protected GameObject arm1_3;
    protected GameObject arm1Lever;
    protected GameObject armTri;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject plate;

    protected Vector3 ang1_1;
    protected Vector3 ang1_2;
    protected Vector3 ang1_3;
    protected Vector3 ang1Lever;
    protected Vector3 angTri;
    protected Vector3 ang2_1;
    protected Vector3 ang2_2;
    protected Vector3 angP;

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
        angle = kinematics_R(y, x);
        arm1_1.transform.localEulerAngles = new Vector3(ang1_1.x, ang1_1.y, angle[0]);
        arm1_2.transform.localEulerAngles = new Vector3(ang1_2.x, ang1_2.y, angle[0] - 180 - (angle[0] - angle[1]));
        arm1_3.transform.localEulerAngles = new Vector3(ang1_3.x, ang1_3.y, angle[0]);
        arm1Lever.transform.localEulerAngles = new Vector3(ang1Lever.x, ang1Lever.y, 180 + (angle[0] - angle[1]));
        armTri.transform.localEulerAngles = new Vector3(angTri.x, angTri.y, -angle[0]);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, -angle[1]);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, angle[0] - angle[1]);
        plate.transform.localEulerAngles= new Vector3(angP.x, angP.y, angle[1] - angle[0] + z);
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

        float dist2 = x * x + y * y;
        float dist = Mathf.Sqrt(dist2);
        float theta1, theta2;

        // 到達不可能
        if (dist > L1 + L2 || dist < Mathf.Abs(L1 - L2))
        {
            ret.Add(0);
            ret.Add(0);
            return ret;
        }

        // --- θ2 ---
        float cos2 = (dist2 - L1 * L1 - L2 * L2) / (2f * L1 * L2);
        cos2 = Mathf.Clamp(cos2, -1f, 1f);

        float sin2 = Mathf.Sqrt(1f - cos2 * cos2);
//        if (!elbowUp) sin2 = -sin2; // 肘下げ解

        theta2 = Mathf.Atan2(sin2, cos2);

        // --- θ1 ---
        float k1 = L1 + L2 * cos2;
        float k2 = L2 * sin2;

        theta1 = Mathf.Atan2(y, x) - Mathf.Atan2(k2, k1);

        ret.Add(90f - (theta1 * Mathf.Rad2Deg));
        ret.Add(theta2 * Mathf.Rad2Deg);
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
        var armTriTmp = children.Find(d => d.name.Contains("W0334712-") || d.name.Contains("W0652636-") || d.name.Contains("W0693785-"));
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
        var arm2_2Tmp = children.Find(d => d.name.Contains("W0656252-") || d.name.Contains("W0693776-"));
        if (arm2_2Tmp != null)
        {
            arm2_2 = arm2_2Tmp.parent.gameObject;
        }

        // プレート W0334721-(ヘッド)
        var plateTmp = children.Find(d => d.name.Contains("W0334721-"));
        if (plateTmp == null)
        {
            if (HeadObject != null)
            {
                plate = HeadObject;
                plate.transform.parent = arm2_1.transform;
                angP = plate.transform.localEulerAngles;
            }
        }
        else
        {
            plate = plateTmp.gameObject;
            plate.transform.parent = arm2_1.transform;
            angP = plate.transform.localEulerAngles;
            // ヘッドセット
            if (HeadObject != null)
            {
                HeadObject.transform.parent = plate.transform;
                head_offset = HeadObject.transform.localEulerAngles.z;
            }
        }


        // 親子関係セット
        arm.transform.position = arm1_1.transform.position;
        arm.transform.localEulerAngles = Vector3.zero;
        arm.transform.localScale = Vector3.one;
        arm1_1.transform.parent = arm.transform;
        arm1_3.transform.parent = arm.transform;
        arm1_2.transform.parent = arm1Lever.transform;
        arm1Lever.transform.parent = arm.transform;
        armTri.transform.parent = arm1_1.transform;
        arm2_1.transform.parent = arm1_1.transform;
        arm2_2.transform.parent = armTri.transform;

        // 初期角度セット
        ang1_1 = arm1_1.transform.localEulerAngles;
        ang1_2 = arm1_2.transform.localEulerAngles;
        ang1_3 = arm1_3.transform.localEulerAngles;
        ang1Lever = arm1Lever.transform.localEulerAngles;
        angTri = armTri.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;

        // アーム長セット
        L1 = Vector3.Distance(Vector3.zero, Vector3.Scale(arm2_1.transform.localPosition, new Vector3(1, 1, 0))) * 1000f;
        L2 = Vector3.Distance(Vector3.zero, Vector3.Scale(plate.transform.localPosition, new Vector3(1, 1, 0))) * 1000f;
    }
}
