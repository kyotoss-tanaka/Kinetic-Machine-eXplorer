using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class MPX_PI : ParallelLink
{
    //    float ARM1_OFFSET2 = -6.56242787f;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public MPX_PI() : base()
    {
        for (var i = 0; i < AXIS_MAX; i++)
        {
            fL[i] = 300;
            fM[i] = 800;
            fH[i] = 240;
            fSH[i] = 70;
        }
        fL[0] = 350;
        fH[0] = 0;
        ARM1_OFFSET = -7.66225566f;
    }

    public override void setTarget(float x, float y, float z)
    {
        angle = kinematics_R(x, -y, z);
        for (var i = 0; i < AXIS_MAX; i++)
        {
            // アーム1の位置
            arm1[i].localEulerAngles = new Vector3(arm1[i].localEulerAngles.x, arm1[i].localEulerAngles.y, angle[i][0]);
            // アーム2の位置
            arm2[i * 2 + 0].localEulerAngles = new Vector3(0, -angle[i][1], angle[i][2]);
            arm2[i * 2 + 1].localEulerAngles = new Vector3(0, angle[i][1] - 180, -angle[i][2]);
            // 連結部の位置
            var rad = angle[i][2] * RADIANS;
            armSpring[i * 2 + 0].localEulerAngles = new Vector3(0, 0, -angle[i][2]);
            armSpring[i * 2 + 0].localPosition = new Vector3(SPRING1_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
            armSpring[i * 2 + 1].localEulerAngles = new Vector3(0, 0, -angle[i][2]);
            armSpring[i * 2 + 1].localPosition = new Vector3(SPRING2_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
        }
        plate.localPosition = new Vector3(x / 1000, -z / 1000, -y / 1000);
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var parallel = new GameObject("MPX_PI");
        parallel.transform.parent = unitSetting.moveObject.transform;
        arm1 = new();
        arm2 = new();
        armSpring = new();

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();
        arm1.AddRange(children.Where(d => d.name.Contains("アーム（1軸用")));
        arm1.AddRange(children.Where(d => d.name.Contains("アーム（2・3軸用")));
        var arm2_children = new List<Transform>();
        arm2_children.Add(children.Find(d => d.name == "13Q1_ﾀﾞｲ2ｱｰﾑ_XMC-Z1400-19-2"));
        arm2_children.Add(children.Find(d => d.name == "13Q1_ﾀﾞｲ2ｱｰﾑ_XMC-Z1400-19-1"));
        arm2_children.Add(children.Find(d => d.name == "13Q1_ﾀﾞｲ2ｱｰﾑ_XMC-Z1400-19-3"));
        for (var i = 0; i < AXIS_MAX; i++)
        {
            var arm2Tmp = arm2_children[i].gameObject.GetComponentsInChildren<Transform>();
            arm2.AddRange(arm2Tmp.Where(d => d.name.Contains("Copy of 第二アームカーボン")));
            armSpring.AddRange(arm2Tmp.Where(d => d.name.Contains("Copy of 第二アームバネアッシ")));
            var gArm1 = new GameObject($"Arm1-{i + 1}");
            InsertParent(gArm1.transform, arm1[i]);
            gArm1.transform.parent = parallel.transform;
            armSpring[i * 2 + 0].parent = arm2[i * 2 + 0];
            armSpring[i * 2 + 1].parent = arm2[i * 2 + 0];
            for (var j = 0; j < 2; j++)
            {
                var gArm2 = new GameObject($"Arm2-{j + 1}");
                InsertParent(gArm2.transform, arm2[i * 2 + j]);
                gArm2.transform.parent = arm1[i];
                gArm2.transform.localEulerAngles = new Vector3(j == 0 ? 90 : 90, gArm2.transform.localEulerAngles.y, gArm2.transform.localEulerAngles.z);
            }
            gArm1.transform.localEulerAngles = new Vector3(gArm1.transform.localEulerAngles.x, gArm1.transform.localEulerAngles.y, 0);
        }
        var tmpPlate = children.FirstOrDefault(d => d.name.Contains("ヘッドプレート")).gameObject;
        plate = tmpPlate.transform;
        // ヘッドセット
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
        }
        /*
        arm1 = new List<GameObject>();
        arm2_1 = new List<GameObject>();
        arm2_2 = new List<GameObject>();

        var children = GetComponentsInChildren<Transform>().ToList();

        // アーム1-1
        var arm1_1Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("アーム（1軸用）-1")));
        var arm1_1Base = new GameObject("ArmBase1");
        var arm1_1Parts = new GameObject("ArmParts1");
        var arm1_1AllParts = arm1_1Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d != arm1_1Parent);
        arm1_1Base.transform.parent = arm1_1Parent.transform;
        arm1_1Base.transform.localPosition = new Vector3(0, 0, 0);
        arm1_1Base.transform.localEulerAngles = new Vector3(0, 0, 0);
        arm1_1Parts.transform.parent = arm1_1Base.transform;
        arm1_1Parts.transform.localPosition = new Vector3(0, 0, 0);
        arm1_1Parts.transform.localEulerAngles = new Vector3(0, 0, 0);
        foreach (var arm in arm1_1AllParts)
        {
            arm.transform.parent = arm1_1Parts.transform;
        }
        arm1.Add(arm1_1Base);

        // アーム1-2
        var arm1_2Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("アーム（2・3軸用）-1")));
        var arm1_2Base = new GameObject("ArmBase1");
        var arm1_2Parts = new GameObject("ArmParts1");
        var arm1_2AllParts = arm1_2Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d != arm1_2Parent);
        arm1_2Base.transform.parent = arm1_2Parent.transform;
        arm1_2Base.transform.localPosition = new Vector3(0, 0, 0);
        arm1_2Base.transform.localEulerAngles = new Vector3(0, 0, 0);
        arm1_2Parts.transform.parent = arm1_2Base.transform;
        arm1_2Parts.transform.localPosition = new Vector3(0, 0, 0);
        arm1_2Parts.transform.localEulerAngles = new Vector3(0, 0, 0);
        foreach (var arm in arm1_2AllParts)
        {
            arm.transform.parent = arm1_2Parts.transform;
        }
        arm1.Add(arm1_2Base);

        // アーム1-3
        var arm1_3Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("アーム（2・3軸用）-2")));
        var arm1_3Base = new GameObject("ArmBase1");
        var arm1_3Parts = new GameObject("ArmParts1");
        var arm1_3AllParts = arm1_3Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d != arm1_3Parent);
        arm1_3Base.transform.parent = arm1_3Parent.transform;
        arm1_3Base.transform.localPosition = new Vector3(0, 0, 0);
        arm1_3Base.transform.localEulerAngles = new Vector3(0, 0, 0);
        arm1_3Parts.transform.parent = arm1_3Base.transform;
        arm1_3Parts.transform.localPosition = new Vector3(0, 0, 0);
        arm1_3Parts.transform.localEulerAngles = new Vector3(0, 0, 0);
        foreach (var arm in arm1_3AllParts)
        {
            arm.transform.parent = arm1_3Parts.transform;
        }
        arm1.Add(arm1_3Base);

        // アーム2-1
        var arm2_1Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("ﾀﾞｲ2ｱｰﾑ_XMC-Z1400-19-2")));
        var arm2_1Arms = arm2_1Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("第二アームカーボン"));
        var arm2_1Springs = arm2_1Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("第二アームバネアッシ") && (d.parent == arm2_1Parent.transform));
        var arm2_1Base = new GameObject("ArmBase2");
        foreach (var spring in arm2_1Springs)
        {
            spring.transform.parent = arm2_1Arms[0];
            armSpring.Add(spring.gameObject);
        }
        arm2_1Base.transform.parent = arm1_1Base.transform;
        arm2_1Base.transform.localPosition = new Vector3(0, 0, 0);
        arm2_1Base.transform.localEulerAngles = new Vector3(0, 0, 0);
        arm2_1Parent.transform.parent = arm2_1Base.transform;
        arm2_1.Add(arm2_1Arms[0].gameObject);
        arm2_2.Add(arm2_1Arms[1].gameObject);

        // アーム2-2
        var arm2_2Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("ﾀﾞｲ2ｱｰﾑ_XMC-Z1400-19-1")));
        var arm2_2Arms = arm2_2Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("第二アームカーボン"));
        var arm2_2Springs = arm2_2Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("第二アームバネアッシ") && (d.parent == arm2_2Parent.transform));
        var arm2_2Base = new GameObject("ArmBase2");
        foreach (var spring in arm2_2Springs)
        {
            spring.transform.parent = arm2_2Arms[0];
            armSpring.Add(spring.gameObject);
        }
        arm2_2Base.transform.parent = arm1_2Base.transform;
        arm2_2Base.transform.localPosition = new Vector3(0, 0, 0);
        arm2_2Base.transform.localEulerAngles = new Vector3(0, 0, 0);
        arm2_2Parent.transform.parent = arm2_2Base.transform;
        arm2_1.Add(arm2_2Arms[0].gameObject);
        arm2_2.Add(arm2_2Arms[1].gameObject);

        // アーム2-3
        var arm2_3Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("ﾀﾞｲ2ｱｰﾑ_XMC-Z1400-19-3")));
        var arm2_3Arms = arm2_3Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("第二アームカーボン"));
        var arm2_3Springs = arm2_3Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("第二アームバネアッシ") && (d.parent == arm2_3Parent.transform));
        var arm2_3Base = new GameObject("ArmBase2");
        foreach (var spring in arm2_3Springs)
        {
            spring.transform.parent = arm2_3Arms[0];
            armSpring.Add(spring.gameObject);
        }
        arm2_3Base.transform.parent = arm1_3Base.transform;
        arm2_3Base.transform.localPosition = new Vector3(0, 0, 0);
        arm2_3Base.transform.localEulerAngles = new Vector3(0, 0, 0);
        arm2_3Parent.transform.parent = arm2_3Base.transform;
        arm2_1.Add(arm2_3Arms[0].gameObject);
        arm2_2.Add(arm2_3Arms[1].gameObject);

        // 初期角度セット
        arm1_1Parent.transform.localEulerAngles = new Vector3(arm1_1Parent.transform.localEulerAngles.x, arm1_1Parent.transform.localEulerAngles.y, 0);
        arm1_2Parent.transform.localEulerAngles = new Vector3(arm1_2Parent.transform.localEulerAngles.x, arm1_2Parent.transform.localEulerAngles.y, 0);
        arm1_3Parent.transform.localEulerAngles = new Vector3(arm1_3Parent.transform.localEulerAngles.x, arm1_3Parent.transform.localEulerAngles.y, 0);
        arm2_1Parent.transform.localEulerAngles = new Vector3(-90, arm2_1Parent.transform.localEulerAngles.y, arm2_1Parent.transform.localEulerAngles.z);
        arm2_2Parent.transform.localEulerAngles = new Vector3(-90, arm2_2Parent.transform.localEulerAngles.y, arm2_2Parent.transform.localEulerAngles.z);
        arm2_3Parent.transform.localEulerAngles = new Vector3(-90, arm2_3Parent.transform.localEulerAngles.y, arm2_3Parent.transform.localEulerAngles.z);

        // プレート
        plate = children.Where(d => d.name.Contains("ヘッドプレート")).ToList()[0].gameObject;
        // ヘッドセット
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
        }

        // 静的バッチングに変更
        var staticObject = children.Find(d => d.name.Contains("駆動部変則120度")).gameObject;
        MeshRenderer[] renderers = staticObject.GetComponentsInChildren<MeshRenderer>();
        GameObject[] batchTargets = new GameObject[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            batchTargets[i] = renderers[i].gameObject;
        }
        // 静的バッチングを実行（親にまとめてバッチング）
        StaticBatchingUtility.Combine(batchTargets, staticObject);
        */
    }
}
