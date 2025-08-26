using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class YF03N4 : ParallelLink
{
    /// <summary>
    /// �R���X�g���N�^
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
            // �A�[��1�̈ʒu
            arm1[i].localEulerAngles = new Vector3(arm1[i].localEulerAngles.x, arm1[i].localEulerAngles.y, -angle[i][0]);
            // �A�[��2�̈ʒu
            arm2[i * 2 + 0].transform.localEulerAngles = new Vector3(0, 90 - angle[i][1], angle[i][2]);
            arm2[i * 2 + 1].transform.localEulerAngles = new Vector3(0, -90 + angle[i][1], -angle[i][2]);
            // �A�����̈ʒu
            var rad = angle[i][2] * RADIANS;
            armSpring[i * 2 + 0].localEulerAngles = new Vector3(0, 0, 90 - angle[i][2]);
            armSpring[i * 2 + 0].localPosition = new Vector3(SPRING1_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
            armSpring[i * 2 + 1].localEulerAngles = new Vector3(0, 0, 90 - angle[i][2]);
            armSpring[i * 2 + 1].localPosition = new Vector3(SPRING2_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
        }
        plate.localPosition = new Vector3(y / 1000, -z / 1000, x / 1000);
    }

    /// <summary>
    /// ���f���č\�z
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
        arm2_children.Add(children.Find(d => d.name.Contains("��d�p���������A�[��^01S1_YF03N4_�ܻ��ޭ���-3")));
        arm2_children.Add(children.Find(d => d.name.Contains("��d�p���������A�[��^01S1_YF03N4_�ܻ��ޭ���-5")));
        arm2_children.Add(children.Find(d => d.name.Contains("��d�p���������A�[��^01S1_YF03N4_�ܻ��ޭ���-4")));

        for (var i = 0; i < AXIS_MAX; i++)
        {
            var gArm1 = new GameObject($"Arm1-{i + 1}");
            arm2_children[i].transform.localEulerAngles = new Vector3(0, arm2_children[i].transform.localEulerAngles.y, arm2_children[i].transform.localEulerAngles.z);
            arm2.AddRange(arm2_children[i].GetComponentsInChildren<Transform>().Where(d => d.name.Contains("YF003N-A031_���A�[��")));
            armSpring.AddRange(arm2_children[i].GetComponentsInChildren<Transform>().Where(d => d.name.Contains("Copy of �X�v�����O���j�b�g")));
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

        // �v���[�g
        plate = children.Where(d => d.name.Contains("�w�b�h�v���[�g")).ToList()[0];
        // �w�b�h�Z�b�g
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
        }

        // �ÓI�o�b�`���O�ɕύX
        var staticObject = children.Find(d => d.name.Contains("YF003N-A031_BASE")).gameObject;
        MeshRenderer[] renderers = staticObject.GetComponentsInChildren<MeshRenderer>();
        GameObject[] batchTargets = new GameObject[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            batchTargets[i] = renderers[i].gameObject;
        }
        // �ÓI�o�b�`���O�����s�i�e�ɂ܂Ƃ߂ăo�b�`���O�j
        StaticBatchingUtility.Combine(batchTargets, staticObject);
    }
}
