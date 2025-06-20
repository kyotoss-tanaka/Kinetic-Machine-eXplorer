using Parameters;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

public class InternalProcessor : AxisMotionBase
{
    /// <summary>
    /// �L�����o�X�\��
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// ����֘AI/O
    /// </summary>
    private class ActionIo
    {
        /// <summary>
        /// �J�nIO
        /// </summary>
        public TagInfo StartInput;
        /// <summary>
        /// ����IO
        /// </summary>
        public TagInfo EndOutput;
        /// <summary>
        /// �g���K�������t���O
        /// </summary>
        public bool isTrigger;
        /// <summary>
        /// ���͔��]
        /// </summary>
        public bool isInputRvs;
        /// <summary>
        /// �l
        /// </summary>
        private bool isValue;
        public void RenewTrigger()
        {
            var isValue = (GlobalScript.GetTagData(StartInput) == (isInputRvs ? 0 : 1));
            /*
            if (!isValue)
            {
                // �w�߂�OFF�Ȃ犮����OFF���Ă���
                GlobalScript.SetTagData(EndOutput, 0);
            }
            */
            isTrigger = isValue;// && !this.isValue;
            this.isValue = isValue;
        }
    }

    /// <summary>
    /// ����Ȑ����
    /// </summary>
    private class ActionCurveInfo
    {
        /// <summary>
        /// �J�n�ʒu
        /// </summary>
        public Vector3 startPos;
        /// <summary>
        /// �ڕW�ʒu
        /// </summary>
        public Vector3 targetPos;
        /// <summary>
        /// �X�g���[�N
        /// </summary>
        public float st;
        /// <summary>
        /// �I�t�Z�b�g
        /// </summary>
        public float offset;
        /// <summary>
        /// �������
        /// </summary>
//        public int dir;
        /// <summary>
        /// ���쎞��
        /// </summary>
        public float totalTime;
        /// <summary>
        /// ��������
        /// </summary>
        public float aclTime;
        /// <summary>
        /// ��������
        /// </summary>
        public float dclTime;
        /// <summary>
        /// �ő呬�x
        /// </summary>
        public float maxSpd;
        /// <summary>
        /// ����g�`
        /// </summary>
        public List<float> actCurve = new List<float>();
        /// <summary>
        /// ����IO
        /// </summary>
        public ActionIo actionIo;
        /// <summary>
        /// �A������
        /// </summary>
        public bool isContinue;
        /// <summary>
        /// �ʒu�擾
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public bool getPosition(float time, ref float pos)
        {
            if (time >= actCurve.Count - 1)
            {
                pos = st;
                return true;
            }
            else if(time <= 0)
            {
                pos = 0;
            }
            else
            {
                int t1 = (int)Math.Floor(time);
                float t2 = time - t1;
                float d1 = actCurve[t1]; 
                float d2 = actCurve[t1 + 1];
                pos = (d2 - d1) * t2 + d1;
            }
            return false;
        }
        /// <summary>
        /// �J�[�u�쐬
        /// </summary>
        public void CreateCurve(UnitAction action, Vector3 pos, float st, ActionIo actionIo)
        {
            this.st = st;
            this.actionIo = actionIo;
            startPos = pos;
            targetPos = action.targetPos;
            actCurve = new List<float>();
            totalTime = action.time * Thousand;
            aclTime = action.aclTime;
            dclTime = action.dclTime;
            maxSpd = action.velocity / Thousand;
            float stroke = Math.Abs(action.stroke);
            float acc_Msec = action.aclVal / Million;
            float dcc_Msec = action.dclVal / Million;

            // �萔�͎��O�v�Z���Ă���
            float mul_maxV_Msec_mTa = maxSpd * aclTime;
            float strokeA = mul_maxV_Msec_mTa / 2f;
            float mTe = totalTime - aclTime - dclTime;
            float mul_maxV_Msec_mTe = maxSpd * mTe;
            float sub_mTb_mMoveT = dclTime - totalTime;
            float sub_mMoveT_mTb = totalTime - dclTime;
            float strokeAB = strokeA + mul_maxV_Msec_mTe;
            float maxV_Msec_Double = 2 * maxSpd;
            float beforMMoveT = totalTime - 1;

            for (int elapsedT = 0; elapsedT < totalTime; elapsedT++)
            {
                float move;

                // t�͑�`�����̎O�p�`��
                if (elapsedT <= aclTime)
                {
                    move = (acc_Msec * elapsedT * elapsedT) / 2f;
                    move = Mathf.Round(move * (st / stroke) * 100000f) / 100000f;
                }
                // ��`�����̎O�p�` + t�͑�`�^�񒆂̎l�p�`��
                else if (elapsedT > aclTime && elapsedT <= sub_mMoveT_mTb)
                {
                    move = strokeA + maxSpd * (elapsedT - aclTime);
                    move = Mathf.Round(move * (st / stroke) * 100000f) / 100000f;
                }
                // ��`�����̎O�p�` + ��`�^�񒆂̎l�p�` + t�͑�`�E���̎O�p�`��
                else if (sub_mMoveT_mTb < elapsedT && elapsedT != beforMMoveT)
                {
                    move = strokeAB + (maxV_Msec_Double - dcc_Msec * (elapsedT + sub_mTb_mMoveT)) * (elapsedT + sub_mTb_mMoveT) / 2f;
                    move = Mathf.Round(move * (st / stroke) * 100000f) / 100000f;
                }
                // t�͏I�_�F��`���̖̂ʐ�(�S�X�g���[�N)
                else
                {
                    move = Mathf.Round(st * 100000f) / 100000f;
                }
                actCurve.Add(move);
            }
        }
    }

    /// <summary>
    /// �ʒu�J���|�W
    /// </summary>
    private class CamPosInfo
    {
        /// <summary>
        /// �ڕW�ʒu
        /// </summary>
        public int Target;
        /// <summary>
        /// �ʒu
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// ����IO
        /// </summary>
        public TagInfo EndOutput;
        /// <summary>
        /// �i�p�w��
        /// </summary>
        public bool AdvanceAngle;
    }

    /// <summary>
    /// ����֘AI/O
    /// </summary>
    private List<ActionIo> actionIos = new List<ActionIo>();

    /// <summary>
    /// ����Ȑ����
    /// </summary>
    private List<ActionCurveInfo> actionCurveInfos = new List<ActionCurveInfo>();

    /// <summary>
    /// ���쒆�Ȑ�
    /// </summary>
    private ActionCurveInfo actionCurve = new ActionCurveInfo();

    /// <summary>
    /// ����Ȑ����
    /// </summary>
    private List<CamPosInfo> camPosInfos = new List<CamPosInfo>();

    /// <summary>
    /// �萔
    /// </summary>
    private bool isMoving = false;

    /// <summary>
    /// �o�߃^�C�}�[
    /// </summary>
    private Stopwatch sw = new Stopwatch();

    /// <summary>
    /// �ʐM�x�ꎞ��
    /// </summary>
    private float delayTime;

    /// <summary>
    /// ���݈ʒu
    /// </summary>
    private float nowSpd;

    /// <summary>
    /// ���ݑ��x
    /// </summary>
    private float nowPos;

    /// <summary>
    /// �O�񑬓x
    /// </summary>
    private float prvPos;

    /// <summary>
    /// ���ݎ���
    /// </summary>
    private long nowTime;

    /// <summary>
    /// �O�񎞊�
    /// </summary>
    private long prvTime;

    /// <summary>
    /// ���������_�̌덷�����̂ŕʂɎ������Ă���
    /// </summary>
    private Vector3 innerPosition = Vector3.zero;

    /// �J�n����
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// ���j�b�g�ݒ肩�瓮��ݒ�X�V
    /// </summary>
    protected override void renewUnitSetting()
    {
        base.renewUnitSetting();

        // �f�[�^������
        actionIos.Clear();
        isMoving = false;

        // �ʐM�x�ꎞ��
        delayTime = unitSetting.actionSetting.delay;

        // �J���|�W����蒼��
        camPosInfos = new List<CamPosInfo>();

        // ���݈ʒu�ێ�
        innerPosition = isRotate ? moveObject.transform.localEulerAngles : moveObject.transform.localPosition;

        // �����ʒu�ێ�
        foreach (var action in unitSetting.actionSetting.actions)
        {
            var actionIo = new ActionIo();
            // ����g���K
            actionIo.StartInput = ScriptableObject.CreateInstance<TagInfo>();
            actionIo.StartInput.Database = unitSetting.Database;
            actionIo.StartInput.MechId = unitSetting.mechId;
            if (action.start[0] == '-')
            {
                actionIo.StartInput.Tag = action.start.Substring(1);
                actionIo.isInputRvs = true;
            }
            else
            {
                actionIo.StartInput.Tag = action.start;
                actionIo.isInputRvs = false;
            }

            // ���슮��
            actionIo.EndOutput = ScriptableObject.CreateInstance<TagInfo>();
            actionIo.EndOutput.Database = unitSetting.Database;
            actionIo.EndOutput.MechId = unitSetting.mechId;
            actionIo.EndOutput.Tag = action.end;
            actionIos.Add(actionIo);

            action.targetPos = moveDir * (action.target * action.dir + action.offset);
            action.targetPos /= Thousand;

            // �J���|�W�쐬
            if (camPosInfos.Find(d => d.Target == action.target) == null)
            {
                camPosInfos.Add(new CamPosInfo
                {
                    Target = action.target,
                    Position = action.targetPos,
                    EndOutput = actionIo.EndOutput
                });
            }

            // ��{����ݒ�
            if (unitSetting.actionSetting.mode == 1)
            {
                // ���Ԑݒ�
                action.aclTime = action.acl * Thousand;
                action.dclTime = action.dcl * Thousand;
                // �ő呬�x���v�Z���čő呬�x���X�g�ɉ�����
                action.velocity = Math.Abs((2 * action.stroke) / (2 * action.time * Thousand - action.aclTime - action.dclTime)) * Thousand;

                // �������x���v�Z���ē���ݒ�Ɋi�[����
                action.aclVal = action.velocity / action.aclTime * Thousand;
                action.dclVal = action.velocity / action.dclTime * Thousand;
            }
            else
            {
                // �����x�ݒ�
                action.aclVal = action.acl * 9800;
                action.dclVal = action.dcl * 9800;
                // ���̌����ɂ����锻�ʎ��̌v�Z���s��
                float a = -(1 / action.aclVal + 1 / action.dclVal);
                float b = 2 * action.time;
                float c = -2 * Math.Abs(action.stroke);
                float discriminant = CommonFunction.Discriminant(a, b, c);

                if (discriminant > 0)
                {
                    // ������
                    action.velocity = CommonFunction.QuadraticFormula_Real(discriminant, a, b, c);
                    // ���������Ԃ��v�Z���ē���ݒ�Ɋi�[����
                    action.aclTime = action.velocity / action.aclVal * Thousand;
                    action.dclTime = action.velocity / action.dclVal * Thousand;
                }
                else
                {
                    // �������̂��ߎ��Ԑݒ�ɕύX
                    action.isChanged = true;
                    action.aclTime = action.time / 2 * Thousand;
                    action.dclTime = action.time / 2 * Thousand;
                    // �ő呬�x���v�Z���čő呬�x���X�g�ɉ�����
                    action.velocity = Math.Abs((2 * action.stroke) / (2 * action.time * Thousand - action.aclTime - action.dclTime)) * Thousand;

                    // �������x���v�Z���ē���ݒ�Ɋi�[����
                    action.aclVal = action.velocity / action.aclTime * Thousand;
                    action.dclVal = action.velocity / action.dclTime * Thousand;
                }
            }
        }

        // �N����OFF
        foreach (var campos in camPosInfos)
        {
            GlobalScript.SetTagData(campos.EndOutput, 0);
        }
    }

    /// <summary>FixedUpdate
    /// �^�C�}�[����
    /// </summary>
    // Update is called once per frame
//    protected override void MyFixedUpdate()
    protected override void FixedUpdate()
    {
        if (moveObject == null)
        {
            return;
        }
        if ((actionIos.Count == 0) || (actionCurve.actCurve == null) || (actionCurve.actionIo == null))
        {
            renewUnitSetting();
        }
        if (!isMoving)
        {
            // �o�ߎ��ԃN���A
            nowTime = 0;
            // �g���K�X�V
            for (int i = 0; i < actionIos.Count; i++)
            {
                actionIos[i].RenewTrigger();
            }
            // ����I/O�`�F�b�N
            for (int i = 0; i < actionIos.Count; i++)
            {
                if (actionIos[i].isTrigger)
                {
                    // ����J�n�g���K���o��
                    sw.Reset();
                    sw.Restart();
                    // �ڕW���W�ƌ��ݍ��W�̋������X�g���[�N�Ƃ��ē���Ȑ����쐬����
                    if (!Generate_ST_Curve(i))
                    {
                        // ���B�ς�
                        continue;
                    }
                    isMoving = true;
                    break;
                }
            }
        }
        else
        {
            // �o�ߑ���
            nowTime = sw.ElapsedMilliseconds;
            // �ʐM�x�ꎞ�ԍ��݂ŏI��IO��ON
            if (nowTime >= actionCurve.actCurve.Count - delayTime)
            {
                // �i�p�w��
                var campos = camPosInfos.Find(d => d.EndOutput.Equals(actionCurve.actionIo.EndOutput));
                if (campos != null)
                {
                    campos.AdvanceAngle = true;
                }
            }
            // �ʒu�擾
            float pos = 0;
            if (actionCurve.getPosition(nowTime, ref pos))
            {
                // ����I��
                if (actionCurve.isContinue)
                {
                    // �p������Ȃ�C���^�[���b�N
                    isMoving = GlobalScript.GetTagData(actionCurve.actionIo.StartInput) != 0;
                }
                else
                {
                    isMoving = false;
                }
                if (isRotate)
                {
                    // ��]����
                    moveObject.transform.localEulerAngles = actionCurve.targetPos * Thousand;
                    innerPosition = actionCurve.targetPos * Thousand;
                }
                else
                {
                    // ��������
                    var position = transform.TransformPoint(actionCurve.targetPos);
                    rb.MovePosition(position);
                    //                    moveObject.transform.localPosition = actionCurve.targetPos;
                    innerPosition = actionCurve.targetPos;
                }
            }
            else
            {
                if (isRotate)
                {
                    // ��]����
                    moveObject.transform.localEulerAngles = actionCurve.startPos * Thousand + pos * moveDir;
                    innerPosition = actionCurve.startPos * Thousand + pos * moveDir;
                    nowPos = Vector3.Distance(Vector3.zero, moveObject.transform.localEulerAngles);
                    if (chuckSetting != null)
                    {
                        foreach (var child in chuckSetting.children)
                        {
                            child.setting.moveObject.transform.localEulerAngles = moveObject.transform.localEulerAngles * child.dir + child.offset * moveDir / Thousand;
                        }
                    }
                }
                else
                {
                    // ��������
                    /*
                    var position = transform.TransformPoint(actionCurve.startPos + pos * moveDir / Thousand);
                    rb.MovePosition(position);
                    */
                    moveObject.transform.localPosition = actionCurve.startPos + pos * moveDir / Thousand;
                    innerPosition = actionCurve.startPos + pos * moveDir / Thousand;
                    nowPos = Vector3.Distance(Vector3.zero, moveObject.transform.localPosition) * Thousand;
                    if (chuckSetting != null)
                    {
                        foreach (var child in chuckSetting.children)
                        {
                            child.setting.moveObject.transform.localPosition = moveObject.transform.localPosition * child.dir + child.offset * moveDir / Thousand;
                        }
                    }
                }
            }
        }
        // �ʒu�擾
        if (isRotate)
        {
            // ��]����
            nowPos = Vector3.Distance(Vector3.zero, moveObject.transform.localEulerAngles);
        }
        else
        {
            // ��������
            nowPos = Vector3.Distance(Vector3.zero, moveObject.transform.localPosition) * Thousand;
        }
        // ���x�Z�o
        if (nowTime - prvTime > 0)
        {
            nowSpd = (nowPos - prvPos) / (nowTime - prvTime) * 1000;
        }

        // �o�͏���
        foreach (var campos in camPosInfos)
        {
            float dist = 0;
            if (isRotate)
            {
                dist = Vector3.Distance(campos.Position * Thousand, innerPosition);
            }
            else
            {
                dist = Vector3.Distance(campos.Position, innerPosition) * Thousand;
            }
            GlobalScript.SetTagData(campos.EndOutput, campos.AdvanceAngle ? 1 : (dist <= 1 ? 1 : 0));
            campos.AdvanceAngle = false;
        }

        // �O��f�[�^�ێ�
        prvTime = nowTime;
        prvPos = nowPos;
    }

    /// <summary>
    /// ST�J�[�u�쐬
    /// </summary>
    /// <param name="action"></param>
    bool Generate_ST_Curve(int index)
    {
        float st = 0;
        var startPos = Vector3.zero;
        var targetPos = Vector3.zero;
        for(var i = 0; i < actionIos.Count; i++)
        {
            var nowIndex = (index + i) % actionIos.Count;
            var nextIndex = (index + i + 1) % actionIos.Count;
            var action = unitSetting.actionSetting.actions[nowIndex];
            var actionIo = actionIos[nowIndex];
            if (i == 0)
            {
                // ����
                targetPos = action.targetPos;
                if (isRotate)
                {
                    // ��]����
                    startPos = innerPosition / Thousand;
                    var prvIndex = (index + actionIos.Count - 1) % actionIos.Count;
                    var diff = (startPos - unitSetting.actionSetting.actions[prvIndex].targetPos) * Thousand;
                    if ((int)Math.Round(diff.x + diff.y + diff.z) % 360 == 0)
                    {
                        // �O��̖ڕW�ʒu�ƈ�v���Ă���̂őO��̖ڕW�ʒu���g�p
                        startPos = unitSetting.actionSetting.actions[prvIndex].targetPos;
                    }
                }
                else
                {
                    // ��������
                    startPos = innerPosition;
                }
                st = Vector3.Distance(targetPos, startPos) * Thousand;
                if (isRotate)
                {
                    // ��]�n��360���Ő��K��
                    st = (int)Math.Round(st) % 360;
                }
            }
            else
            {
                // �A�����쎞
                startPos = targetPos;
                targetPos = action.targetPos;
                st = Vector3.Distance(targetPos, startPos) * Thousand;
            }
            if (st <= 0.001)
            {
                // 0.1m�ȉ��͓���Ȃ�
                return false;
            }
            var direction = (action.targetPos - startPos).normalized;
            if (direction.x + direction.y + direction.z < 0)
            {
                // �t�]
                st = -st;
            }
            var curve = actionCurveInfos.Find(d => d.startPos == startPos && d.targetPos == targetPos);
            if (curve == null)
            {
                // �g�`�쐬
                curve = new ActionCurveInfo();
                curve.CreateCurve(action, startPos, st, actionIo);
                actionCurveInfos.Add(curve);
            }
            if (i == 0)
            {
                actionCurve.isContinue = false;
                actionCurve.startPos = startPos;
                actionCurve.actionIo = actionIo;
                actionCurve.offset = action.offset;
                actionCurve.actCurve.Clear();
                actionCurve.actCurve.AddRange(curve.actCurve);
            }
            else
            {
                var endPoint = actionCurve.actCurve[actionCurve.actCurve.Count - 1];
                // ��~���ԕ��ǉ�
                for(var j = 0; j < action.stop * Thousand; j++)
                {
                    actionCurve.actCurve.Add(endPoint);
                }
                // ����ǉ�
                foreach (var tmp in curve.actCurve)
                {
                    actionCurve.actCurve.Add(tmp + endPoint);
                }
            }
            actionCurve.targetPos = targetPos;
            if (!unitSetting.actionSetting.actions[nextIndex].isContinue)
            {
                break;
            }
            actionCurve.isContinue = true;
        }
        return true;
    }

    /// <summary>
    /// �L�����o�X�\���p�f�[�^�쐬
    /// </summary>
    public override void RenewCanvasValues()
    {
        base.RenewCanvasValues();
        dctDispValue["Status"] = new CanvasValue
        {
            value = isMoving ? "Run" : "Stop"
        };
        dctDispValue["Time"] = new CanvasValue
        {
            value = nowTime,
            unit = "msec"
        };
        dctDispValue["Position"] = new CanvasValue
        {
            value = nowPos,
            format = "0.0",
            unit = "mm"
        };
        dctDispValue["Speed"] = new CanvasValue
        {
            value = nowSpd,
            format = "0.0",
            unit = "mm/sec"
        };
    }
}
