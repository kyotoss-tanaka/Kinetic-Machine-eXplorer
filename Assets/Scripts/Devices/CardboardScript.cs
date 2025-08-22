using Meta.XR.ImmersiveDebugger.UserInterface;
using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// �i�{�[���p�X�N���v�g
/// </summary>
public class CardboardScript : KssBaseScript
{
    [Serializable]
    public class CardboardParts
    {
        [SerializeField]
        public string name;
        [SerializeField]
        public GameObject parts;
        [SerializeField]
        public CardboardPartsScript script;
        [SerializeField]
        public Vector3 anchor = new();
        [SerializeField]
        public Vector3 axis = new Vector3(1, 0, 0);
        [SerializeField]
        public ActionTableData actionTableData;
        [SerializeField]
        public bool isFlap = false;
        [SerializeField]
        public decimal value;
    }

    [Serializable]
    public class CardboardSize
    {

        [SerializeField]
        public int L_Width;
        [SerializeField]
        public int W_Width;
        [SerializeField]
        public int Body_Height;
        [SerializeField]
        public int Top_Height;
        [SerializeField]
        public int Bottom_Height;
    }


    [Serializable]
    public class SuckInfo
    {
        public SuctionScript suctionScript;
        public CardboardParts parts;
    }

    /// <summary>
    /// �i�{�[���ݒ�
    /// </summary>
    [SerializeField]
    protected CardboardSetting cardboardSetting;

    /// <summary>
    /// ���[�h 0:L1/W1 1:L2/W2 1:L1/L2 2:W1:W2
    /// </summary>
    [SerializeField]
    protected int mode;

    /// <summary>
    /// ���ݎ���
    /// </summary>
    [SerializeField]
    protected int time;

    /// <summary>
    /// ���ݎ���
    /// </summary>
    [SerializeField]
    protected int startTime;

    /// <summary>
    /// ���݃T�C�N��
    /// </summary>
    [SerializeField]
    protected int cycle;

    /// <summary>
    /// Body�ԋ���
    /// </summary>
    [SerializeField]
    protected float distance;

    /// <summary>
    /// �T�C�Y
    /// </summary>
    [SerializeField]
    protected CardboardSize Size;

    /// <summary>
    /// �z�������
    /// </summary>
    [SerializeField]
    protected List<SuckInfo> suckInfos = new();

    /// <summary>
    /// �S���i
    /// </summary>
    [SerializeField]
    protected List<CardboardParts> cardboardParts = new();

    [SerializeField]
    CardboardParts L1_Body;
    [SerializeField]
    CardboardParts L1_Top;
    [SerializeField]
    CardboardParts L1_Bottom;
    [SerializeField]
    CardboardParts L2_Body;
    [SerializeField]
    CardboardParts L2_Top;
    [SerializeField]
    CardboardParts L2_Bottom;
    [SerializeField]
    CardboardParts W1_Body;
    [SerializeField]
    CardboardParts W1_Top;
    [SerializeField]
    CardboardParts W1_Bottom;
    [SerializeField]
    CardboardParts W2_Body;
    [SerializeField]
    CardboardParts W2_Top;
    [SerializeField]
    CardboardParts W2_Bottom;

    /// <summary>
    /// �T�C�N���^�O
    /// </summary>
    protected TagInfo cycleTag;

    /// <summary>
    /// Rigidbody
    /// </summary>
    private Rigidbody rigi = null;

    /// <summary>
    /// �J�n����
    /// </summary>
    protected override void Start()
    {
        base.Start();

        // ����������
        Initialize();
    }

    /// <summary>
    /// ��������
    /// </summary>
    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        time = GlobalScript.GetTagData(cycleTag);
        if (startTime < 0)
        {
            cycle = time % (cardboardSetting.cycle <= 0 ? 1000 : cardboardSetting.cycle);
        }
        else
        {
            cycle = time - startTime;
        }
        // Body
        if (suckInfos.Count > 0)
        {
            // �z������Ă���Ƃ��̂ݒi�{�[������
            if (startTime < 0)
            {
                // �T�C�N���̓��[�v���Ȃ�
                startTime = time - cycle;
            }
        }
        if (startTime >= 0)
        {
            foreach (var parts in cardboardParts)
            {
                if ((parts.actionTableData != null) && (parts.actionTableData.datas.Count > 0))
                {
                    parts.value = 0;
                    var before = parts.actionTableData.datas.LastOrDefault(d => d.time <= cycle);
                    var after = parts.actionTableData.datas.FirstOrDefault(d => d.time >= cycle);
                    if (before != null && after != null && before.time != after.time)
                    {
                        parts.value = before.value + (after.value - before.value) * (cycle - before.time) / (after.time - before.time);
                    }
                    else
                    {
                        parts.value = before != null ? before.value : (after != null ? after.value : parts.value);
                    }
                    if (parts.isFlap)
                    {
                        // �t���b�v�Ȃ�
                        parts.parts.transform.localEulerAngles = (float)parts.value * parts.axis;
                    }
                }
            }
            if ((L1_Body.actionTableData != null) && (L1_Body.actionTableData.datas.Count > 0))
            {
                var value = (float)L1_Body.value;
                if (mode == 0)
                {
                    // L1�
                    W1_Body.parts.transform.localEulerAngles = (180 - value) * W1_Body.axis;
                    W2_Body.parts.transform.localEulerAngles = (180 - value) * W2_Body.axis;
                    L2_Body.parts.transform.localEulerAngles = value * L2_Body.axis;
                }
                else
                {
                    // L2�
                    W2_Body.parts.transform.localEulerAngles = (180 - value) * W2_Body.axis;
                    W1_Body.parts.transform.localEulerAngles = (180 - value) * W1_Body.axis;
                    L1_Body.parts.transform.localEulerAngles = value * L1_Body.axis;
                }
                /*
                value = 0;
                var before = actionTableData.datas.LastOrDefault(d => d.time <= cycle);
                var after = actionTableData.datas.FirstOrDefault(d => d.time >= cycle);
                if (before != null && after != null && before.time != after.time)
                {
                    value = before.value + (after.value - before.value) * (cycle - before.time) / (after.time - before.time);
                }
                else
                {
                    value = before != null ? before.value : (after != null ? after.value : value);
                }
                position = (float)(value + offset) / (rate == 0 ? 1000f : rate) * unitSetting.actionSetting.dir;
                if (isRotate)
                {
                    moveObject.transform.localEulerAngles = moveDir * position;
                }
                else
                {
                    moveObject.transform.localPosition = moveDir * position;
                }
                */
            }
        }
        /*
        var vctAngle = Vector3.zero;
        if (suckInfos.Count == 2)
        {
            // ���s
            var box1 = suckInfos[0].parts.script.boxCollider;
            var box2 = suckInfos[1].parts.script.boxCollider;
            // ���������Ă���Ƃ��̂ݏ���
            if (mode >= 2)
            {
                if (LinePlaneIntersection(box1, box2))
                {
                    if (mode == 2)
                    {
                        if (distance > Size.W_Width)
                        {
                            angle = 90;
                        }
                        else
                        {
                            angle = (float)(Math.Asin(distance / Size.W_Width) * 180 / Math.PI);
                        }
                        vctAngle = angle * W1_Body.axis;
                        W1_Body.parts.transform.localEulerAngles = vctAngle;
                        vctAngle = angle * W2_Body.axis;
                        W2_Body.parts.transform.localEulerAngles = vctAngle;
                    }
                    else if (mode == 3)
                    {
                        if (distance > Size.L_Width)
                        {
                            angle = 90;
                        }
                        else
                        {
                            angle = (float)(Math.Asin(distance / Size.L_Width) * 180 / Math.PI);
                        }
                        vctAngle = (180 - angle) * L1_Body.axis;
                        L1_Body.parts.transform.localEulerAngles = vctAngle;
                        vctAngle = (180 - angle) * L2_Body.axis;
                        L2_Body.parts.transform.localEulerAngles = vctAngle;
                    }
                }
            }
            else
            {
                // ���p
                Vector3 normal1 = GetThinAxisNormal(box1);
                Vector3 normal2 = GetThinAxisNormal(box2);
                angle = Vector3.Angle(normal1, normal2);
                if (mode == 0)
                {
                    vctAngle = angle * W2_Body.axis;
                    W2_Body.parts.transform.localEulerAngles = vctAngle;
                    vctAngle = (180 - angle) * L2_Body.axis;
                    L2_Body.parts.transform.localEulerAngles = vctAngle;
                }
                else if (mode == 1)
                {
                    vctAngle = angle * W1_Body.axis;
                    W1_Body.parts.transform.localEulerAngles = vctAngle;
                    vctAngle = (180 - angle) * L1_Body.axis;
                    L1_Body.parts.transform.localEulerAngles = vctAngle;
                }
            }
        }
        */
    }
    /*
    /// <summary>
    /// ��̃R���C�_�[�̋�����}��
    /// </summary>
    /// <param name="boxA"></param>
    /// <param name="boxB"></param>
    /// <returns></returns>
    public bool LinePlaneIntersection(BoxCollider boxA, BoxCollider boxB)
    {
        Vector3 normal = GetThinAxisNormal(boxA);

        Vector3 p0 = boxA.ClosestPoint(boxB.transform.position);
        Vector3 dir = -1 * normal;
        Vector3 planePoint = boxB.ClosestPoint(boxA.transform.position);
        Vector3 planeNormal = normal;
        Vector3 intersection = Vector3.zero;
        distance = 0;

        float denom = Vector3.Dot(dir, planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            // dir �� planeNormal ������ �� ���s�Ȃ̂Ō������Ȃ�
            return false;
        }

        float t = Vector3.Dot(planePoint - p0, planeNormal) / denom;
        if (t < 0)
        {
            // ��_�͒����̋t�����i�K�v�ɉ����Ĕ���j
            // �����ł͂Ȃ��u�������v�ƍl�������ꍇ�� false
        }

        intersection = p0 + dir * t;
        distance = Vector3.Distance(p0, intersection) * 1000;
        return true;
    }

    public static Vector3 GetThinAxisNormal(BoxCollider box)
    {
        Vector3 size = box.size;
        Vector3 scale = box.transform.lossyScale;

        // �e���̃��[���h�X�P�[���T�C�Y�i= �����ڂ̌��݁j
        float sx = Mathf.Abs(size.x * scale.x);
        float sy = Mathf.Abs(size.y * scale.y);
        float sz = Mathf.Abs(size.z * scale.z);

        // �ŏ����𔻒肵�āA���[���h��Ԃ̕����x�N�g����Ԃ�
        if (sx <= sy && sx <= sz)
            return box.transform.right;      // �X����X��
        else if (sy <= sx && sy <= sz)
            return box.transform.up;         // �X����Y��
        else
            return box.transform.forward;    // �X����Z��
    }
    */
    /// <summary>
    /// ����������
    /// </summary>
    private void Initialize()
    {
        // �T�C�N���^�O�ݒ�
        var tag = GlobalScript.callbackTags.Find(d => d.database == unitSetting.Database);
        cycleTag = tag == null ? null : tag.cycle;
        startTime = -1;

        // Rigidbody�ǉ�
        rigi = GetComponent<Rigidbody>();
        if (rigi == null)
        {
            rigi = gameObject.AddComponent<Rigidbody>();
        }
        rigi.isKinematic = false;
        rigi.useGravity = true;

        // �t���b�v�̐e�q�֌W�ݒ�
        L1_Top.parts.transform.parent = L1_Body.parts.transform;
        L1_Bottom.parts.transform.parent = L1_Body.parts.transform;
        L2_Top.parts.transform.parent = L2_Body.parts.transform;
        L2_Bottom.parts.transform.parent = L2_Body.parts.transform;
        W1_Top.parts.transform.parent = W1_Body.parts.transform;
        W1_Bottom.parts.transform.parent = W1_Body.parts.transform;
        W2_Top.parts.transform.parent = W2_Body.parts.transform;
        W2_Bottom.parts.transform.parent = W2_Body.parts.transform;

        // �{�f�B�̐e�q�֌W
        if (mode == 0)
        {
            // L1��ŊJ��
            W2_Body.parts.transform.parent = L1_Body.parts.transform;
            L2_Body.parts.transform.parent = W2_Body.parts.transform;
            W1_Body.parts.transform.parent = L2_Body.parts.transform;
        }
        else
        {
            // L2��ŊJ��
            W1_Body.parts.transform.parent = L2_Body.parts.transform;
            L1_Body.parts.transform.parent = W1_Body.parts.transform;
            W2_Body.parts.transform.parent = L1_Body.parts.transform;
        }
        /*
        // ���[�h�ʐe�q�֌W�ݒ�
        if (mode == 1)
        {
            L1_Body.parts.transform.parent = W2_Body.parts.transform;
            W1_Body.parts.transform.parent = L1_Body.parts.transform;
        }
        else if (mode == 2)
        {
            W1_Body.parts.transform.parent = L1_Body.parts.transform;
            W2_Body.parts.transform.parent = L2_Body.parts.transform;
        }
        else if (mode == 3)
        {
            L1_Body.parts.transform.parent = W2_Body.parts.transform;
            L2_Body.parts.transform.parent = W1_Body.parts.transform;
        }
        else
        {
            L2_Body.parts.transform.parent = W1_Body.parts.transform;
            W2_Body.parts.transform.parent = L2_Body.parts.transform;
        }
        */

        // �e�ݒ�
        L1_Top.isFlap = true;
        L1_Bottom.isFlap = true;
        L2_Top.isFlap = true;
        L2_Bottom.isFlap = true;
        W1_Top.isFlap = true;
        W1_Bottom.isFlap = true;
        W2_Top.isFlap = true;
        W2_Bottom.isFlap = true;

        cardboardParts.Add(L1_Body);
        cardboardParts.Add(L1_Top);
        cardboardParts.Add(L1_Bottom);
        cardboardParts.Add(L2_Body);
        cardboardParts.Add(L2_Top);
        cardboardParts.Add(L2_Bottom);
        cardboardParts.Add(W1_Body);
        cardboardParts.Add(W1_Top);
        cardboardParts.Add(W1_Bottom);
        cardboardParts.Add(W2_Body);
        cardboardParts.Add(W2_Top);
        cardboardParts.Add(W2_Bottom);
        foreach (var parts in cardboardParts)
        {
            SetComponent(parts);
        }
    }

    /// <summary>
    /// �R���|�[�l���g�Z�b�g
    /// </summary>
    /// <param name="parts"></param>
    private void SetComponent(CardboardParts parts)
    {
        parts.script = parts.parts.AddComponent<CardboardPartsScript>();
        parts.script.isFlap = parts.isFlap;

        // �e�[�u���f�[�^�擾
        var unit = unitSetting.name + ":";
        parts.actionTableData = GlobalScript.actionTableDatas.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unit + parts.name));
        if (parts.actionTableData == null)
        {
            parts.actionTableData = new ActionTableData();
        }
        else
        {
            // ���Ԃ��ƂɃ\�[�g
            parts.actionTableData.datas = parts.actionTableData.datas.OrderBy(d => d.time).ToList();
        }
    }

    /// <summary>
    /// �z���Z�b�g
    /// </summary>
    public bool SetSuction(SuctionScript suction, GameObject parts)
    {
        if (parts != null)
        {
            var p = cardboardParts.Find(d => d.parts == parts);
            if (!p.isFlap)
            {
                var info = new CardboardScript.SuckInfo
                {
                    suctionScript = suction,
                    parts = p
                };
                /*
                if (suckInfos.Count == 0)
                {
                    transform.parent = suction.transform;
                }
                else
                {
                    parts.transform.parent = suction.transform;
                }
                */
                suckInfos.Add(info);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// �z���Z�b�g
    /// </summary>
    public void ResetSuction(SuctionScript suction)
    {
        var info = suckInfos.Find(d => d.suctionScript == suction);
        if (info != null)
        {
            suckInfos.Remove(info);
            /*
            if (suckInfos.Count > 0)
            {
                transform.parent = suckInfos[0].suctionScript.transform;
                suckInfos[0].parts.parts.transform.parent = transform;
            }
            else
            {
                transform.parent = null;
                rigi.useGravity = true;
                rigi.isKinematic = false;
            }
            */
        }
    }

    /// <summary>
    /// �p�����[�^�Z�b�g
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public void SetParameter(CardboardScript org)
    {
        SetParameter(org.GetUnitSetting(), org.GetSetting());
    }

    /// <summary>
    /// �p�����[�^�Z�b�g
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        cardboardSetting = (CardboardSetting)obj;
        mode = cardboardSetting.mode;
        var children = GetComponentsInChildren<Transform>().Select(d => d.gameObject).ToList();
        L1_Body = new CardboardParts
        {
            name = "Body",
            parts = children.Find(d => d.name == cardboardSetting.l1_Body),
            axis = new Vector3(0, 1, 0)
        };
        L1_Top = new CardboardParts
        {
            name = "L1_Top",
            parts = children.Find(d => d.name == cardboardSetting.l1_Top),
            axis = new Vector3(-1, 0, 0)
        };
        L1_Bottom = new CardboardParts
        {
            name = "L1_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.l1_Bottom),
            axis = new Vector3(1, 0, 0)
        };
        L2_Body = new CardboardParts
        {
            parts = children.Find(d => d.name == cardboardSetting.l2_Body),
            axis = new Vector3(0, 1, 0)
        };
        L2_Top = new CardboardParts
        {
            name = "L2_Top",
            parts = children.Find(d => d.name == cardboardSetting.l2_Top),
            axis = new Vector3(-1, 0, 0)
        };
        L2_Bottom = new CardboardParts
        {
            name = "L2_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.l2_Bottom),
            axis = new Vector3(1, 0, 0)
        };
        W1_Body = new CardboardParts
        {
            parts = children.Find(d => d.name == cardboardSetting.w1_Body),
            axis = new Vector3(0, 1, 0)
        };
        W1_Top = new CardboardParts
        {
            name = "W1_Top",
            parts = children.Find(d => d.name == cardboardSetting.w1_Top),
            axis = new Vector3(-1, 0, 0)
        };
        W1_Bottom = new CardboardParts
        {
            name = "W1_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.w1_Bottom),
            axis = new Vector3(1, 0, 0)
        };
        W2_Body = new CardboardParts
        {
            parts = children.Find(d => d.name == cardboardSetting.w2_Body),
            axis = new Vector3(0, 1, 0)
        };
        W2_Top = new CardboardParts
        {
            name = "W2_Top",
            parts = children.Find(d => d.name == cardboardSetting.w2_Top),
            axis = new Vector3(-1, 0, 0)
        };
        W2_Bottom = new CardboardParts
        {
            name = "W2_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.w2_Bottom),
            axis = new Vector3(1, 0, 0)
        };
    }

    /// <summary>
    /// �ݒ�擾
    /// </summary>
    /// <returns></returns>
    public UnitSetting GetUnitSetting()
    {
        return unitSetting;
    }

    /// <summary>
    /// �ݒ�擾
    /// </summary>
    /// <returns></returns>
    public CardboardSetting GetSetting()
    {
        return cardboardSetting;
    }
}
