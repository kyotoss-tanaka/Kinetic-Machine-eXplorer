using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class YF03N4 : ParallelLink
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    public YF03N4() : base()
    {
        for (var i = 0; i < AXIS_MAX; i++)
        {
            fL[i] = 380;
            fM[i] = 920;
            fH[i] = 180;
            fSH[i] = 65;
        }
        SPRING1_OFFSET_X = 0.037f;
        SPRING2_OFFSET_X = 0.883f;
        SPRING_OFFSET_Y = 0.055f;
    }

    public override void setTarget(float x, float y, float z)
    {
        angle = kinematics_R(x, -y, z);
        for (var i = 0; i < AXIS_MAX; i++)
        {
            // アーム1の位置
            arm1[i].localEulerAngles = new Vector3(arm1[i].localEulerAngles.x, arm1[i].localEulerAngles.y, -angle[i][0]);
            // アーム2の位置
            arm2[i * 2 + 0].transform.localEulerAngles = new Vector3(0, 90 - angle[i][1], angle[i][2]);
            arm2[i * 2 + 1].transform.localEulerAngles = new Vector3(0, -90 + angle[i][1], -angle[i][2]);
            // 連結部の位置
            var rad = angle[i][2] * RADIANS;
            armSpring[i * 2 + 0].localEulerAngles = new Vector3(0, 0, 90 - angle[i][2]);
            armSpring[i * 2 + 0].localPosition = new Vector3(SPRING1_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
            armSpring[i * 2 + 1].localEulerAngles = new Vector3(0, 0, 90 - angle[i][2]);
            armSpring[i * 2 + 1].localPosition = new Vector3(SPRING2_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
        }
        plate.localPosition = new Vector3(y / 1000, -z / 1000, x / 1000);
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var parallel = new GameObject("YF03N4");
        parallel.transform.parent = unitSetting.moveObject.transform;
        arm1 = new();
        arm2 = new();
        armSpring = new();

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        arm1.Add(children.Find(d => d.name.Contains("YF003N-A031_J1")));
        arm1.Add(children.Find(d => d.name.Contains("YF003N-A031_J3")));
        arm1.Add(children.Find(d => d.name.Contains("YF003N-A031_J2")));

        var arm2_children = new List<Transform>();
        arm2_children.Add(children.Find(d => d.name.Contains("川重パラレル第二アーム^01S1_YF03N4_ｶﾜｻｷｼﾞｭｳｺｳ-3")));
        arm2_children.Add(children.Find(d => d.name.Contains("川重パラレル第二アーム^01S1_YF03N4_ｶﾜｻｷｼﾞｭｳｺｳ-5")));
        arm2_children.Add(children.Find(d => d.name.Contains("川重パラレル第二アーム^01S1_YF03N4_ｶﾜｻｷｼﾞｭｳｺｳ-4")));

        for (var i = 0; i < AXIS_MAX; i++)
        {
            var gArm1 = new GameObject($"Arm1-{i + 1}");
            arm2_children[i].transform.localEulerAngles = new Vector3(0, arm2_children[i].transform.localEulerAngles.y, arm2_children[i].transform.localEulerAngles.z);
            arm2.AddRange(arm2_children[i].GetComponentsInChildren<Transform>().Where(d => d.name.Contains("YF003N-A031_第二アーム")));
            armSpring.AddRange(arm2_children[i].GetComponentsInChildren<Transform>().Where(d => d.name.Contains("Copy of スプリングユニット")));
            InsertParent(gArm1.transform, arm1[i]);
            gArm1.transform.parent = parallel.transform;
            for (var j = 0; j < 2; j++)
            {
                var gArm2 = new GameObject($"Arm2-{j + 1}");
                InsertParent(gArm2.transform, arm2[i * 2 + j]);
                gArm2.transform.parent = arm1[i];
            }
            armSpring[i * 2 + 0].parent = arm2[i * 2 + 0];
            armSpring[i * 2 + 1].parent = arm2[i * 2 + 0];
        }

        // プレート
        plate = children.Where(d => d.name.Contains("ヘッドプレート")).ToList()[0];
        // ヘッドセット
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
        }

        // 静的バッチングに変更
        var staticObject = children.Find(d => d.name.Contains("YF003N-A031_BASE")).gameObject;
        MeshRenderer[] renderers = staticObject.GetComponentsInChildren<MeshRenderer>();
        GameObject[] batchTargets = new GameObject[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            batchTargets[i] = renderers[i].gameObject;
        }
        // 静的バッチングを実行（親にまとめてバッチング）
        StaticBatchingUtility.Combine(batchTargets, staticObject);
    }
}
