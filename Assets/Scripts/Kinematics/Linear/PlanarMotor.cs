using Parameters;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using static Br6DScript;

public class PlanarMotor : UseHeadBaseScript
{
    #region �v���p�e�B
    [SerializeField]
    protected GameObject LinearObject;

    [SerializeField]
    protected int OpcUaCh;

    [SerializeField]
    protected TagInfo X;

    [SerializeField]
    protected TagInfo Y;

    [SerializeField]
    protected TagInfo Z;

    [SerializeField]
    protected TagInfo RX;

    [SerializeField]
    protected TagInfo RY;

    [SerializeField]
    protected TagInfo RZ;

    [SerializeField]
    protected int Count = 10;

    [SerializeField]
    protected Vector3 PositionOffset;

    [SerializeField]
    protected Vector3 EulerAnglesOffset;

    /// <summary>
    /// �x�[�X�I�u�W�F�N�g
    /// </summary>
    protected GameObject objBase;

    /// <summary>
    /// �ݒ�
    /// </summary>
    protected PlanarMotorSetting pm;

    #endregion �v���p�e�B

    /// <summary>
    /// ���񏈗�����
    /// </summary>
    private bool IsFirst = true;

    /// <summary>
    /// �V���g��
    /// </summary>
    protected List<GameObject> shuttles = new List<GameObject>();

    /// <summary>
    /// �V���g���^�O���
    /// </summary>
    protected List<ShuttleTagInfo> shuttleTags = new List<ShuttleTagInfo>();

    #region �֐�
    /// <summary>
    /// �J�n����
    /// </summary>
    protected override void Start()
    {
        base.Start();
        // ���j�A�I�u�W�F�N�g����U�폜
        if (LinearObject != null)
        {
            // ��x�폜����
            Destroy(LinearObject);

            // �Đ����p
            objBase = new GameObject("MoverFuctory");
            objBase.transform.parent = unitSetting.unitObject.transform;
            objBase.transform.localPosition = new();
            objBase.transform.localEulerAngles = new();

            for (var i = 0; i < Count; i++)
            {
                var sh = Instantiate(LinearObject);
                sh.transform.parent = objBase.transform;
                sh.transform.localPosition = new Vector3();
                sh.transform.eulerAngles = new Vector3();
                var del = sh.GetComponent<ObjectDeleteScript>();
                if (del != null)
                {
                    Destroy(del);
                    foreach (var wk in unitSetting.workDeleteSettings)
                    {
                        var s = sh.transform.AddComponent<ObjectDeleteScript>();
                        s.SetParameter(unitSetting, wk);
                    }
                }
                shuttles.Add(sh);
                shuttleTags.Add(new ShuttleTagInfo());
            }
        }
    }

    /// <summary>
    /// �J�n������
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.FixedUpdate();

        if (objBase != null)
        {
            objBase.transform.localPosition = PositionOffset;
            objBase.transform.localEulerAngles = EulerAnglesOffset;
            for (var i = 0; i < shuttles.Count; i++)
            {
                if (shuttleTags.Count != shuttles.Count)
                {
                    // �f�o�b�O�΍�
                    shuttleTags.Add(new ShuttleTagInfo());
                }
                var x = GetTagValue(pm.tags_p[0], ref shuttleTags[i].X, i);
                var y = GetTagValue(pm.tags_p[1], ref shuttleTags[i].Y, i);
                var z = GetTagValue(pm.tags_p[2], ref shuttleTags[i].Z, i);
                var rx = GetTagValue(pm.tags_r[0], ref shuttleTags[i].RX, i);
                var ry = GetTagValue(pm.tags_r[1], ref shuttleTags[i].RY, i);
                var rz = GetTagValue(pm.tags_r[2], ref shuttleTags[i].RZ, i);
                SetTarget(i,
                    x / 1000000f * pm.dir_p[0], y / 1000000f * pm.dir_p[1], z / 1000000f * pm.dir_p[2],
                    rx / 1000000f * pm.dir_r[0], ry / 1000000f * pm.dir_r[1], rz / 1000000f * pm.dir_r[2]
                );
            }
        }
    }

    /// <summary>
    /// ���W�Z�b�g
    /// </summary>
    /// <param name="index"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="rx"></param>
    /// <param name="ry"></param>
    /// <param name="rz"></param>
    public virtual void SetTarget(int index, float x, float y, float z, float rx, float ry, float rz)
    {
    }

    /// <summary>
    /// �g�p���Ă���^�O���擾����
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        var ret = base.GetUseTags();
        ret.Add(X);
        ret.Add(Y);
        ret.Add(Z);
        ret.Add(RX);
        ret.Add(RY);
        ret.Add(RZ);
        return ret;
    }

    /// <summary>
    /// �p�����[�^���Z�b�g����
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        pm = (PlanarMotorSetting)obj;

        LinearObject = pm.moverUnit == null ? null : pm.moverUnit.moveObject;

        Count = pm.count;
        PositionOffset = new Vector3
        {
            x = pm.offset_p[0],
            y = pm.offset_p[2],
            z = pm.offset_p[1]
        };
        EulerAnglesOffset = new Vector3
        {
            x = pm.offset_r[0],
            y = pm.offset_r[2],
            z = pm.offset_r[1]
        };
    }
    #endregion �֐�
}
