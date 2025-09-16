using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class MPS2_3AS : ParallelLink
{
    /// <summary>
    /// �R���X�g���N�^
    /// </summary>
    public MPS2_3AS() : base()
    {
        for (var i = 0; i < AXIS_MAX; i++)
        {
            fL[i] = 350;
            fM[i] = 800;
            fH[i] = 200;
            fSH[i] = 45;
        }
        SPRING1_OFFSET_X = 0.035f;
        SPRING2_OFFSET_X = 0.765f;
        SPRING_OFFSET_Y = 0.05f;
    }

    public override void SetTarget(float x, float y, float z)
    {
        y = -y;
        var tmp = kinematics_R(x, y, z);
        // �A�[�����ƂɊp�x����ւ�
        angle = new List<List<float>> { tmp[2], tmp[1], tmp[0] };
        for (var i = 0; i < AXIS_MAX; i++)
        {
            // �A�[��1�̈ʒu
            arm1[i].localEulerAngles = new Vector3(arm1[i].localEulerAngles.x, arm1[i].localEulerAngles.y, angle[i][0]);
            // �A�[��2�̈ʒu
            arm2[i * 2 + 0].localEulerAngles = new Vector3(0, -angle[i][1], angle[i][2]);
            arm2[i * 2 + 1].localEulerAngles = new Vector3(0, angle[i][1], -angle[i][2]);
            // �A�����̈ʒu
            var rad = angle[i][2] * RADIANS;
            armSpring[i * 2 + 0].localEulerAngles = new Vector3(0, 0, -angle[i][2]);
            armSpring[i * 2 + 0].localPosition = new Vector3(SPRING1_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
            armSpring[i * 2 + 1].localEulerAngles = new Vector3(0, 0, -angle[i][2]);
            armSpring[i * 2 + 1].localPosition = new Vector3(SPRING2_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
        }
        plate.localPosition = new Vector3(-y / 1000, -z / 1000, x / 1000);
    }

    /// <summary>
    /// ���f���č\�z
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var parallel = new GameObject("MPS2_3AS");
        parallel.transform.parent = unitSetting.moveObject.transform;
        arm1 = new();
        arm2 = new();
        armSpring = new();

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();
        arm1.AddRange(children.Where(d => d.name.Contains("���A�[��")));
        arm2.AddRange(children.Where(d => d.name.Contains("Copy of ���A�[���J�[�{��")));
        armSpring.AddRange(children.Where(d => d.name.Contains("Copy of ���A�[���o�l�A�b�V")));
        for (var i = 0; i < AXIS_MAX; i++)
        {
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
                gArm2.transform.localEulerAngles = new Vector3(j == 0 ? 90 : -90, gArm2.transform.localEulerAngles.y, gArm2.transform.localEulerAngles.z);
            }
            gArm1.transform.localEulerAngles = new Vector3(gArm1.transform.localEulerAngles.x, gArm1.transform.localEulerAngles.y, 0);
        }
        var tmpPlate = children.FirstOrDefault(d => d.name.Contains("�O�p�v���[�g")).gameObject;
        plate = tmpPlate.transform;
        // �w�b�h�Z�b�g
        if (HeadObject != null)
        {
            HeadObject.transform.parent = plate.transform;
        }
    }
}
