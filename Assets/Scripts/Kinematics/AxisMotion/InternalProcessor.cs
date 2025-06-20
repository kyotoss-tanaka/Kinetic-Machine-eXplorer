using Parameters;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

public class InternalProcessor : AxisMotionBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// 動作関連I/O
    /// </summary>
    private class ActionIo
    {
        /// <summary>
        /// 開始IO
        /// </summary>
        public TagInfo StartInput;
        /// <summary>
        /// 完了IO
        /// </summary>
        public TagInfo EndOutput;
        /// <summary>
        /// トリガ発生中フラグ
        /// </summary>
        public bool isTrigger;
        /// <summary>
        /// 入力反転
        /// </summary>
        public bool isInputRvs;
        /// <summary>
        /// 値
        /// </summary>
        private bool isValue;
        public void RenewTrigger()
        {
            var isValue = (GlobalScript.GetTagData(StartInput) == (isInputRvs ? 0 : 1));
            /*
            if (!isValue)
            {
                // 指令がOFFなら完了もOFFしておく
                GlobalScript.SetTagData(EndOutput, 0);
            }
            */
            isTrigger = isValue;// && !this.isValue;
            this.isValue = isValue;
        }
    }

    /// <summary>
    /// 動作曲線情報
    /// </summary>
    private class ActionCurveInfo
    {
        /// <summary>
        /// 開始位置
        /// </summary>
        public Vector3 startPos;
        /// <summary>
        /// 目標位置
        /// </summary>
        public Vector3 targetPos;
        /// <summary>
        /// ストローク
        /// </summary>
        public float st;
        /// <summary>
        /// オフセット
        /// </summary>
        public float offset;
        /// <summary>
        /// 動作方向
        /// </summary>
//        public int dir;
        /// <summary>
        /// 動作時間
        /// </summary>
        public float totalTime;
        /// <summary>
        /// 加速時間
        /// </summary>
        public float aclTime;
        /// <summary>
        /// 減速時間
        /// </summary>
        public float dclTime;
        /// <summary>
        /// 最大速度
        /// </summary>
        public float maxSpd;
        /// <summary>
        /// 動作波形
        /// </summary>
        public List<float> actCurve = new List<float>();
        /// <summary>
        /// 動作IO
        /// </summary>
        public ActionIo actionIo;
        /// <summary>
        /// 連続動作
        /// </summary>
        public bool isContinue;
        /// <summary>
        /// 位置取得
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
        /// カーブ作成
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

            // 定数は事前計算しておく
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

                // tは台形左側の三角形内
                if (elapsedT <= aclTime)
                {
                    move = (acc_Msec * elapsedT * elapsedT) / 2f;
                    move = Mathf.Round(move * (st / stroke) * 100000f) / 100000f;
                }
                // 台形左側の三角形 + tは台形真ん中の四角形内
                else if (elapsedT > aclTime && elapsedT <= sub_mMoveT_mTb)
                {
                    move = strokeA + maxSpd * (elapsedT - aclTime);
                    move = Mathf.Round(move * (st / stroke) * 100000f) / 100000f;
                }
                // 台形左側の三角形 + 台形真ん中の四角形 + tは台形右側の三角形内
                else if (sub_mMoveT_mTb < elapsedT && elapsedT != beforMMoveT)
                {
                    move = strokeAB + (maxV_Msec_Double - dcc_Msec * (elapsedT + sub_mTb_mMoveT)) * (elapsedT + sub_mTb_mMoveT) / 2f;
                    move = Mathf.Round(move * (st / stroke) * 100000f) / 100000f;
                }
                // tは終点：台形自体の面積(全ストローク)
                else
                {
                    move = Mathf.Round(st * 100000f) / 100000f;
                }
                actCurve.Add(move);
            }
        }
    }

    /// <summary>
    /// 位置カムポジ
    /// </summary>
    private class CamPosInfo
    {
        /// <summary>
        /// 目標位置
        /// </summary>
        public int Target;
        /// <summary>
        /// 位置
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// 完了IO
        /// </summary>
        public TagInfo EndOutput;
        /// <summary>
        /// 進角指令
        /// </summary>
        public bool AdvanceAngle;
    }

    /// <summary>
    /// 動作関連I/O
    /// </summary>
    private List<ActionIo> actionIos = new List<ActionIo>();

    /// <summary>
    /// 動作曲線情報
    /// </summary>
    private List<ActionCurveInfo> actionCurveInfos = new List<ActionCurveInfo>();

    /// <summary>
    /// 動作中曲線
    /// </summary>
    private ActionCurveInfo actionCurve = new ActionCurveInfo();

    /// <summary>
    /// 動作曲線情報
    /// </summary>
    private List<CamPosInfo> camPosInfos = new List<CamPosInfo>();

    /// <summary>
    /// 定数
    /// </summary>
    private bool isMoving = false;

    /// <summary>
    /// 経過タイマー
    /// </summary>
    private Stopwatch sw = new Stopwatch();

    /// <summary>
    /// 通信遅れ時間
    /// </summary>
    private float delayTime;

    /// <summary>
    /// 現在位置
    /// </summary>
    private float nowSpd;

    /// <summary>
    /// 現在速度
    /// </summary>
    private float nowPos;

    /// <summary>
    /// 前回速度
    /// </summary>
    private float prvPos;

    /// <summary>
    /// 現在時間
    /// </summary>
    private long nowTime;

    /// <summary>
    /// 前回時間
    /// </summary>
    private long prvTime;

    /// <summary>
    /// 浮動小数点の誤差が乗るので別に持たせておく
    /// </summary>
    private Vector3 innerPosition = Vector3.zero;

    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    protected override void renewUnitSetting()
    {
        base.renewUnitSetting();

        // データ初期化
        actionIos.Clear();
        isMoving = false;

        // 通信遅れ時間
        delayTime = unitSetting.actionSetting.delay;

        // カムポジ情報作り直し
        camPosInfos = new List<CamPosInfo>();

        // 現在位置保持
        innerPosition = isRotate ? moveObject.transform.localEulerAngles : moveObject.transform.localPosition;

        // 初期位置保持
        foreach (var action in unitSetting.actionSetting.actions)
        {
            var actionIo = new ActionIo();
            // 動作トリガ
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

            // 動作完了
            actionIo.EndOutput = ScriptableObject.CreateInstance<TagInfo>();
            actionIo.EndOutput.Database = unitSetting.Database;
            actionIo.EndOutput.MechId = unitSetting.mechId;
            actionIo.EndOutput.Tag = action.end;
            actionIos.Add(actionIo);

            action.targetPos = moveDir * (action.target * action.dir + action.offset);
            action.targetPos /= Thousand;

            // カムポジ作成
            if (camPosInfos.Find(d => d.Target == action.target) == null)
            {
                camPosInfos.Add(new CamPosInfo
                {
                    Target = action.target,
                    Position = action.targetPos,
                    EndOutput = actionIo.EndOutput
                });
            }

            // 基本動作設定
            if (unitSetting.actionSetting.mode == 1)
            {
                // 時間設定
                action.aclTime = action.acl * Thousand;
                action.dclTime = action.dcl * Thousand;
                // 最大速度を計算して最大速度リストに加える
                action.velocity = Math.Abs((2 * action.stroke) / (2 * action.time * Thousand - action.aclTime - action.dclTime)) * Thousand;

                // 加減速度を計算して動作設定に格納する
                action.aclVal = action.velocity / action.aclTime * Thousand;
                action.dclVal = action.velocity / action.dclTime * Thousand;
            }
            else
            {
                // 加速度設定
                action.aclVal = action.acl * 9800;
                action.dclVal = action.dcl * 9800;
                // 解の公式における判別式の計算を行う
                float a = -(1 / action.aclVal + 1 / action.dclVal);
                float b = 2 * action.time;
                float c = -2 * Math.Abs(action.stroke);
                float discriminant = CommonFunction.Discriminant(a, b, c);

                if (discriminant > 0)
                {
                    // 実数解
                    action.velocity = CommonFunction.QuadraticFormula_Real(discriminant, a, b, c);
                    // 加減速時間を計算して動作設定に格納する
                    action.aclTime = action.velocity / action.aclVal * Thousand;
                    action.dclTime = action.velocity / action.dclVal * Thousand;
                }
                else
                {
                    // 虚数解のため時間設定に変更
                    action.isChanged = true;
                    action.aclTime = action.time / 2 * Thousand;
                    action.dclTime = action.time / 2 * Thousand;
                    // 最大速度を計算して最大速度リストに加える
                    action.velocity = Math.Abs((2 * action.stroke) / (2 * action.time * Thousand - action.aclTime - action.dclTime)) * Thousand;

                    // 加減速度を計算して動作設定に格納する
                    action.aclVal = action.velocity / action.aclTime * Thousand;
                    action.dclVal = action.velocity / action.dclTime * Thousand;
                }
            }
        }

        // 起動時OFF
        foreach (var campos in camPosInfos)
        {
            GlobalScript.SetTagData(campos.EndOutput, 0);
        }
    }

    /// <summary>FixedUpdate
    /// タイマー処理
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
            // 経過時間クリア
            nowTime = 0;
            // トリガ更新
            for (int i = 0; i < actionIos.Count; i++)
            {
                actionIos[i].RenewTrigger();
            }
            // 入力I/Oチェック
            for (int i = 0; i < actionIos.Count; i++)
            {
                if (actionIos[i].isTrigger)
                {
                    // 動作開始トリガ検出中
                    sw.Reset();
                    sw.Restart();
                    // 目標座標と現在座標の距離をストロークとして動作曲線を作成する
                    if (!Generate_ST_Curve(i))
                    {
                        // 到達済み
                        continue;
                    }
                    isMoving = true;
                    break;
                }
            }
        }
        else
        {
            // 経過測定
            nowTime = sw.ElapsedMilliseconds;
            // 通信遅れ時間込みで終了IOをON
            if (nowTime >= actionCurve.actCurve.Count - delayTime)
            {
                // 進角指令
                var campos = camPosInfos.Find(d => d.EndOutput.Equals(actionCurve.actionIo.EndOutput));
                if (campos != null)
                {
                    campos.AdvanceAngle = true;
                }
            }
            // 位置取得
            float pos = 0;
            if (actionCurve.getPosition(nowTime, ref pos))
            {
                // 動作終了
                if (actionCurve.isContinue)
                {
                    // 継続動作ならインターロック
                    isMoving = GlobalScript.GetTagData(actionCurve.actionIo.StartInput) != 0;
                }
                else
                {
                    isMoving = false;
                }
                if (isRotate)
                {
                    // 回転動作
                    moveObject.transform.localEulerAngles = actionCurve.targetPos * Thousand;
                    innerPosition = actionCurve.targetPos * Thousand;
                }
                else
                {
                    // 直線動作
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
                    // 回転動作
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
                    // 直線動作
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
        // 位置取得
        if (isRotate)
        {
            // 回転動作
            nowPos = Vector3.Distance(Vector3.zero, moveObject.transform.localEulerAngles);
        }
        else
        {
            // 直線動作
            nowPos = Vector3.Distance(Vector3.zero, moveObject.transform.localPosition) * Thousand;
        }
        // 速度算出
        if (nowTime - prvTime > 0)
        {
            nowSpd = (nowPos - prvPos) / (nowTime - prvTime) * 1000;
        }

        // 出力処理
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

        // 前回データ保持
        prvTime = nowTime;
        prvPos = nowPos;
    }

    /// <summary>
    /// STカーブ作成
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
                // 初回
                targetPos = action.targetPos;
                if (isRotate)
                {
                    // 回転動作
                    startPos = innerPosition / Thousand;
                    var prvIndex = (index + actionIos.Count - 1) % actionIos.Count;
                    var diff = (startPos - unitSetting.actionSetting.actions[prvIndex].targetPos) * Thousand;
                    if ((int)Math.Round(diff.x + diff.y + diff.z) % 360 == 0)
                    {
                        // 前回の目標位置と一致しているので前回の目標位置を使用
                        startPos = unitSetting.actionSetting.actions[prvIndex].targetPos;
                    }
                }
                else
                {
                    // 直線動作
                    startPos = innerPosition;
                }
                st = Vector3.Distance(targetPos, startPos) * Thousand;
                if (isRotate)
                {
                    // 回転系は360°で正規化
                    st = (int)Math.Round(st) % 360;
                }
            }
            else
            {
                // 連続動作時
                startPos = targetPos;
                targetPos = action.targetPos;
                st = Vector3.Distance(targetPos, startPos) * Thousand;
            }
            if (st <= 0.001)
            {
                // 0.1m以下は動作なし
                return false;
            }
            var direction = (action.targetPos - startPos).normalized;
            if (direction.x + direction.y + direction.z < 0)
            {
                // 逆転
                st = -st;
            }
            var curve = actionCurveInfos.Find(d => d.startPos == startPos && d.targetPos == targetPos);
            if (curve == null)
            {
                // 波形作成
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
                // 停止時間分追加
                for(var j = 0; j < action.stop * Thousand; j++)
                {
                    actionCurve.actCurve.Add(endPoint);
                }
                // 動作追加
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
    /// キャンバス表示用データ作成
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
