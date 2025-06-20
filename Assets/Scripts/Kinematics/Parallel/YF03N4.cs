using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class YF03N4 : ParallelLink
{
    /// <summary>
    /// �R���X�g���N�^
    /// </summary>
    public YF03N4()
    {
        for (var i = 0; i < AXIS_MAX; i++){
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
        var angle = kinematics_R(x, y, z);
        for (var i = 0; i < AXIS_MAX; i++)
        {
            // �A�[��1�̈ʒu
            arm1[i].transform.localEulerAngles = new Vector3(arm1[i].transform.localEulerAngles.x, arm1[i].transform.localEulerAngles.y, -angle[i][0]);
            // �A�[��2�̈ʒu
            arm2_1[i].transform.localEulerAngles = new Vector3(0, angle[i][1], angle[i][2]);
            arm2_2[i].transform.localEulerAngles = new Vector3(180, angle[i][1], -angle[i][2]);
            // �A�����̈ʒu
            var rad = angle[i][2] * RADIANS;
            armSpring[i * 2 + 0].transform.localEulerAngles = new Vector3(0, 0, 90 - angle[i][2]);
            armSpring[i * 2 + 0].transform.localPosition = new Vector3(SPRING1_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
            armSpring[i * 2 + 1].transform.localEulerAngles = new Vector3(0, 0, 90 - angle[i][2]);
            armSpring[i * 2 + 1].transform.localPosition = new Vector3(SPRING2_OFFSET_X + SPRING_OFFSET_Y * Mathf.Sin(rad), SPRING_OFFSET_Y * Mathf.Cos(rad), 0);
        }
        plate.transform.localPosition = new Vector3(y / 1000, -z / 1000, -x / 1000);
    }

    /// <summary>
    /// ���f���č\�z
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        arm1 = new List<GameObject>();
        arm2_1 = new List<GameObject>();
        arm2_2 = new List<GameObject>();

        var children = GetComponentsInChildren<Transform>().ToList();

        // �A�[��1-1
        var arm1_1Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("YF003N-A031_J1")));
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

        // �A�[��1-3
        var arm1_3Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("YF003N-A031_J3")));
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

        // �A�[��1-2
        var arm1_2Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("YF003N-A031_J2")));
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

        // �A�[��2-1
        var arm2_1Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("��d�p���������A�[��^01S1_YF03N4_�ܻ��ޭ���-3")));
        var arm2_1Arms = arm2_1Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => (d.transform.parent == arm2_1Parent.transform) && d.name.Contains("���A�[��") && !d.name.Contains("�X�v�����O���j�b�g"));
        var arm2_1Springs = arm2_1Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("�X�v�����O���j�b�g") && (d.parent == arm2_1Parent.transform));
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

        // �A�[��2-3
        var arm2_3Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("��d�p���������A�[��^01S1_YF03N4_�ܻ��ޭ���-5")));
        var arm2_3Arms = arm2_3Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => (d.transform.parent == arm2_3Parent.transform) && d.name.Contains("���A�[��") && !d.name.Contains("�X�v�����O���j�b�g"));
        var arm2_3Springs = arm2_3Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("�X�v�����O���j�b�g") && (d.parent == arm2_3Parent.transform));
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

        // �A�[��2-2
        var arm2_2Parent = children.Find(d => (d.transform.parent == this.transform) && (d.name.Contains("��d�p���������A�[��^01S1_YF03N4_�ܻ��ޭ���-4")));
        var arm2_2Arms = arm2_2Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => (d.transform.parent == arm2_2Parent.transform) && d.name.Contains("���A�[��") && !d.name.Contains("�X�v�����O���j�b�g"));
        var arm2_2Springs = arm2_2Parent.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("�X�v�����O���j�b�g") && (d.parent == arm2_2Parent.transform));
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

        // �����p�x�Z�b�g
        arm1_1Parent.transform.localEulerAngles = new Vector3(arm1_1Parent.transform.localEulerAngles.x, arm1_1Parent.transform.localEulerAngles.y, 0);
        arm1_2Parent.transform.localEulerAngles = new Vector3(arm1_2Parent.transform.localEulerAngles.x, arm1_2Parent.transform.localEulerAngles.y, 0);
        arm1_3Parent.transform.localEulerAngles = new Vector3(arm1_3Parent.transform.localEulerAngles.x, arm1_3Parent.transform.localEulerAngles.y, 0);
        arm2_1Parent.transform.localEulerAngles = new Vector3(-90, arm2_1Parent.transform.localEulerAngles.y, arm2_1Parent.transform.localEulerAngles.z);
        arm2_2Parent.transform.localEulerAngles = new Vector3(-90, arm2_2Parent.transform.localEulerAngles.y, arm2_2Parent.transform.localEulerAngles.z);
        arm2_3Parent.transform.localEulerAngles = new Vector3(-90, arm2_3Parent.transform.localEulerAngles.y, arm2_3Parent.transform.localEulerAngles.z);

        // �v���[�g
        plate = children.Where(d => d.name.Contains("�w�b�h�v���[�g")).ToList()[0].gameObject;
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
