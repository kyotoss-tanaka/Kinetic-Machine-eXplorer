using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MPS2_4AS : ParallelLink
{
    private float BASE_OFFSET = 0.228f;

    public override void SetTarget(float x, float y, float z)
    {
        /*
        var angle = kinematics_R(x, y, z);
        for (var i = 0; i < AXIS_MAX; i++)
        {
            // アーム1の位置
            arm1[i].transform.localEulerAngles = new Vector3(arm1[i].transform.localEulerAngles.x, arm1[i].transform.localEulerAngles.y, angle[i][0]);
            // アーム2の位置
            arm2_1[i].transform.localEulerAngles = new Vector3(0, -angle[i][1], angle[i][2]);
            arm2_2[i].transform.localEulerAngles = new Vector3(0, angle[i][1], -angle[i][2]);
        }
        plate.transform.localPosition = new Vector3(y / 1000, x / 1000, z / 1000);
        */
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        /*
        arm1 = new List<GameObject>();
        arm2_1 = new List<GameObject>();
        arm2_2 = new List<GameObject>();

        var children = GetComponentsInChildren<Transform>();
        var allArm1 = children.Where(d => d.name.Contains("ARM")).Select(d => d.gameObject).ToList();
        var allArm2 = children.Where(d => d.name.Contains("ROD")).Select(d => d.gameObject).ToList();

        // ベースオブジェクト取得
        baseObject = children.FirstOrDefault(d => d.name.Contains("BASE")).gameObject;

        // 可動範囲オブジェクト
        var area = children.FirstOrDefault(d => d.name.Contains("可動範囲")).gameObject;

        // クローン用アーム取得
        var clnArm1 = allArm1.FindAll(d => d.name.Contains("-1"));
        var clnArm2_1 = allArm2.FindAll(d => d.name.Contains("-3"));
        var clnArm2_2 = allArm2.FindAll(d => d.name.Contains("-4"));

        var tmpObj = new GameObject("ArmBase");
        tmpObj.transform.parent = baseObject.transform;
        tmpObj.transform.localPosition = new Vector3(0, 0, 0);
        tmpObj.transform.localEulerAngles = new Vector3(0, 180, 0);
        for (var i = 0; i < AXIS_MAX; i++)
        {
            var tmpArm1 = Instantiate(clnArm1[0], tmpObj.transform);
            tmpArm1.transform.localPosition = new Vector3(fH[i] / 1000 * Mathf.Sin(ARM_RAD_OFFSET[i]), -BASE_OFFSET, fH[i] / 1000 * Mathf.Cos(ARM_RAD_OFFSET[i]));
            tmpArm1.transform.localEulerAngles = new Vector3(0, ARM_OFFSET[i] + 90, ARM1_OFFSET);
            var cArm1 = tmpArm1.GetComponentsInChildren<Transform>();
            arm1.Add(cArm1[1].gameObject);
            var tmpArm2_1Base = new GameObject("Arm2_1Base");
            tmpArm2_1Base.transform.parent = cArm1[1].transform;
            tmpArm2_1Base.transform.localPosition = new Vector3(-fL[i] / 1000, -0.03049998f, 0.05f);
            tmpArm2_1Base.transform.localEulerAngles = new Vector3(0, 0, 180 - ARM1_OFFSET);
            var tmpArm2_1 = Instantiate(clnArm2_1[0], tmpArm2_1Base.transform);
            tmpArm2_1.transform.localPosition = new Vector3(0, 0, 0);
            tmpArm2_1.transform.localEulerAngles = new Vector3(-90, 0, 0);
            var cArm2_1 = tmpArm2_1.GetComponentsInChildren<Transform>();
            arm2_1.Add(cArm2_1[1].gameObject);
            var tmpArm2_2Base = new GameObject("Arm2_2Base");
            tmpArm2_2Base.transform.parent = cArm1[1].transform;
            tmpArm2_2Base.transform.localPosition = new Vector3(-fL[i] / 1000, -0.03049998f, -0.05f);
            tmpArm2_2Base.transform.localEulerAngles = new Vector3(0, 0, 180 - ARM1_OFFSET);
            var tmpArm2_2 = Instantiate(clnArm2_2[0], tmpArm2_2Base.transform);
            tmpArm2_2.transform.localPosition = new Vector3(0, 0, 0);
            tmpArm2_2.transform.localEulerAngles = new Vector3(90, 0, 0);
            var cArm2_2 = tmpArm2_2.GetComponentsInChildren<Transform>();
            arm2_2.Add(cArm2_2[1].gameObject);
        }
        var tmpPlate = children.FirstOrDefault(d => d.name.Contains("HEAD(0)")).gameObject;
        tmpPlate.transform.localPosition = new Vector3(0, -BASE_OFFSET, 0);
        tmpPlate.transform.localEulerAngles = new Vector3(0, 0, 0);
        var tmpPlateBase = new GameObject("ArmPlateBase");
        tmpPlateBase.transform.parent = tmpPlate.transform;
        tmpPlateBase.transform.localPosition = new Vector3(0, 0, 0);
        tmpPlateBase.transform.localEulerAngles = new Vector3(90, 180, 0);
        plate = tmpPlate.GetComponentsInChildren<Transform>()[1].gameObject;
        plate.transform.parent = tmpPlateBase.transform;
        plate.transform.localPosition = new Vector3(0, 0, 0);
        plate.transform.localEulerAngles = new Vector3(0, 0, 0);
        var joint1 = children.FirstOrDefault(d => d.name.Contains("HEAD^")).gameObject;
        joint1.transform.parent = plate.transform;
        joint1.transform.localPosition = new Vector3(0, 0, 0);
        joint1.transform.localEulerAngles = new Vector3(0, 0, 0);
        var joint2 = children.FirstOrDefault(d => d.name.Contains("lower")).gameObject;
        joint2.transform.parent = plate.transform;
        joint2.transform.localPosition = new Vector3(0, 0, 0);
        joint2.transform.localEulerAngles = new Vector3(0, 0, 0);

        // 不要なアームを削除
        foreach (var arm in allArm1)
        {
            Destroy(arm.gameObject);
        }
        foreach (var arm in allArm2)
        {
            Destroy(arm.gameObject);
        }
        Destroy(area);
        */
    }
}
