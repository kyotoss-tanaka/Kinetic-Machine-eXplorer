using Parameters;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

public class MotionInternal : AxisMotionBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// 動作関連I/O
    /// </summary>
    [Serializable]
    public class ActionIo
    {
        /// <summary>
        /// 開始IO
        /// </summary>
        [SerializeField]
        public TagInfo _StartInput;
        /// <summary>
        /// 完了IO
        /// </summary>
        [SerializeField]
        public TagInfo _EndOutput;
        /// <summary>
        /// 開始IO名
        /// </summary
        [SerializeField]
        public string start;
        /// <summary>
        /// 完了IO名
        /// </summary>
        [SerializeField]
        public string end;
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
        public void RenewTrigger(int value)
        {
            var isValue = (value == (isInputRvs ? 0 : 1));
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
    public class ActionCurveInfo
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
                if (stroke == 0)
                {
                    actCurve.Add(0);
                }
                else
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
    }

    /// <summary>
    /// 位置カムポジ
    /// </summary>
    private class CamPosInfo
    {
        /// <summary>
        /// 目標位置
        /// </summary>
        public float Target;
        /// <summary>
        /// 位置
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// 完了IO名
        /// </summary>
        public string end;
        /// <summary>
        /// 完了IO
        /// </summary>
        public TagInfo _EndOutput;
        /// <summary>
        /// 進角指令
        /// </summary>
        public bool AdvanceAngle;
    }

    /// <summary>
    /// 動作関連I/O
    /// </summary>
    [SerializeField]
    private List<ActionIo> actionIos;

    /// <summary>
    /// 動作曲線情報
    /// </summary>
    [SerializeField]
    private List<ActionCurveInfo> actionCurveInfos = new List<ActionCurveInfo>();

    /// <summary>
    /// 動作中曲線
    /// </summary>
    [SerializeField]
    public ActionCurveInfo actionCurve = new ActionCurveInfo();

    /// <summary>
    /// 動作曲線情報
    /// </summary>
    [SerializeField]
    private List<CamPosInfo> camPosInfos = new List<CamPosInfo>();

    /// <summary>
    /// 定数
    /// </summary>
    [SerializeField]
    private bool isMoving = false;

    /// <summary>
    /// 動作を始めた時刻（Stopwatch.GetTimestamp）
    /// </summary>
    private long startTs;

    /// <summary>
    /// 判定（Tick）と状態を守るロック。外部通信のシリンダは通信スレッドからも判定するため
    /// </summary>
    private readonly object logicLock = new object();

    /// <summary>
    /// 最後の判定の始めに動作中だったか（バケットの見た目に使う）
    /// </summary>
    private bool tickWasMoving;

    /// <summary>
    /// 最後の判定の波形上の位置（バケットの見た目に使う）
    /// </summary>
    private float tickPos;

    /// <summary>
    /// バケットか（判定用。RenewMoveDir で決める）
    /// </summary>
    private bool isBacketLogic;

    /// <summary>
    /// 見た目に反映した位置（変わった時だけ Transform に書く）
    /// </summary>
    private Vector3 appliedPosition;

    /// <summary>
    /// 判定を登録した通信（外部通信のシリンダ）
    /// </summary>
    private ComProtocolBase comTickerCom;

    /// <summary>
    /// 通信へ登録した判定
    /// </summary>
    private Action<long> comTicker;

    /// <summary>
    /// 通信へ登録するかを確認済みか
    /// </summary>
    private bool comTickerChecked;

    /// <summary>
    /// 通信スレッドが最後に判定した時刻（Stopwatch.GetTimestamp）
    /// </summary>
    private long lastComTickTs;

    /// <summary>
    /// 通信遅れ時間
    /// </summary>
    private float delayTime;

    /// <summary>
    /// 外部通信の応答遅れの計測（開始IOを受けてから完了IOを送るまで）
    /// </summary>
    private class ResponseLog
    {
        /// <summary>開始IOの通信先</summary>
        public ComProtocolBase startCom;
        /// <summary>完了IOの通信先</summary>
        public ComProtocolBase endCom;
        /// <summary>開始IOを取り込んだ受信の開始時刻</summary>
        public long recvTs;
        /// <summary>開始IOに気づいた時刻</summary>
        public long detectTs;
        /// <summary>完了IOをONにした時刻（0=まだ）</summary>
        public long onTs;
        /// <summary>開始から完了IOをONにするまでの予定時間(ms)＝動作時間－通信遅れ時間</summary>
        public float expectedMs;
        /// <summary>計測中の FixedUpdate の最大間隔(ms)</summary>
        public float maxGapMs;
        /// <summary>開始IO名</summary>
        public string start;
        /// <summary>完了IO名</summary>
        public string end;
    }

    /// <summary>
    /// 計測中の応答遅れ
    /// </summary>
    private ResponseLog responseLog;

    /// <summary>
    /// 前回の FixedUpdate の時刻（Stopwatch.GetTimestamp）
    /// </summary>
    private long prvFixedTs;

    /// <summary>
    /// 応答遅れをログに出す閾値(ms)。予定より これ以上遅れた時だけ出す
    /// </summary>
    private const float ResponseLogThresholdMs = 100f;

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
    [SerializeField]
    private long nowTime;

    /// <summary>
    /// 前回時間
    /// </summary>
    [SerializeField]
    private long prvTime;

    /// <summary>
    /// 時間オフセット
    /// </summary>
    private long timeOffset;

    /// <summary>
    /// 浮動小数点の誤差が乗るので別に持たせておく
    /// </summary>
    private Vector3 innerPosition = Vector3.zero;

    /// <summary>
    /// 回り続ける軸（全部の動作が連続＝動作がサイクル一杯つながっているバケット）。
    /// 全部が連続だと内部タイムチャートは開始タグを一度も ON にしない（連続の動作は前の動作の続きとして飛ばす）。
    /// また先頭だけ開始にしても、動作の長さがサイクルと同じため次の開始が前の受け渡し中に来て1サイクルおきに取りこぼす。
    /// そこで開始タグを待たず、PLC と同じく各動作を trg の時刻に始めるものとして、サイクル時刻から位置を決める
    /// </summary>
    private bool isLoopAxis;

    /// <summary>
    /// 回り続ける軸の動作ごとの波形（開始位置 0 から、その動作のストロークまで）
    /// </summary>
    private List<ActionCurveInfo> loopCurves = new List<ActionCurveInfo>();

    /// <summary>
    /// 回り続ける軸のサイクル(ms)
    /// </summary>
    private long loopCycle;

    /// <summary>
    /// 回り続ける軸の1サイクルの進み(mm)
    /// </summary>
    private double loopStrokeSum;

    /// <summary>
    /// サイクルタグが無い時の時刻
    /// </summary>
    private Stopwatch loopSw = new Stopwatch();

    /// <summary>
    /// 回り続ける軸の前回の進み(mm・周長で割る前)と時刻（速度表示用）
    /// </summary>
    private double loopPrvTravel;
    private long loopPrvTime;

    /// <summary>
    /// 現在位置(外部タイムチャート参照時)
    /// </summary>
    public float nowValue
    {
        get
        {
            if ((moveDir == Vector3.right) || (moveDir == Vector3.left))
            {
                return innerPosition.x;
            }
            else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
            {
                return innerPosition.y;
            }
            else 
            {
                return innerPosition.z;
            }
        }
    }

    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    public override void RenewMoveDir()
    {
        // 通信スレッドの判定と重ならないようにロックし、登録は作り直してからやり直す（EnsureComTicker）
        lock (logicLock)
        {
            RemoveComTicker();
            RenewMoveDirCore();
            appliedPosition = innerPosition;
            // 判定は通信スレッドからも呼ぶので、Unity の比較を含む isBacket はここで決めておく
            isBacketLogic = isBacket;
        }
    }

    /// <summary>
    /// ユニット設定から動作設定更新（本体）
    /// </summary>
    private void RenewMoveDirCore()
    {
        base.RenewMoveDir();

        // データ初期化
        if (actionIos == null)
        {
            actionIos = new();
        }
        foreach (var actionIo in actionIos)
        {
            actionIo._StartInput = null;
            actionIo._EndOutput = null;
        }
        actionIos.Clear();
        isMoving = false;

        // サイクルタグ設定
        var tag = GlobalScript.callbackTags.Find(d => d.database == unitSetting.Database);
        cycleTag = tag == null ? null : (tag.cycle.Tag == "" ? null : tag.cycle);

        // 通信遅れ時間
        delayTime = unitSetting.actionSetting.delay;

        // カムポジ情報作り直し
        camPosInfos = new List<CamPosInfo>();

        // 現在位置保持
        if (isBacket)
        {
        }
        else
        {
            innerPosition = exModeChange ? Vector3.zero : (isRotate ? moveObject.transform.localEulerAngles : moveObject.transform.localPosition);
        }
        // 初期位置保持
        foreach (var action in unitSetting.actionSetting.actions)
        {
            var actionIo = new ActionIo();
            // 動作トリガ
            if ((action.start != "") && (action.start[0] == '-'))
            {
                actionIo.start = action.start.Substring(1);
                actionIo.isInputRvs = true;
            }
            else
            {
                actionIo.start = action.start;
                actionIo.isInputRvs = false;
            }

            // 動作完了
            actionIo.end = action.end;
            actionIos.Add(actionIo);
            if (exModeChange)
            {
                // 出力モード変更時は拡張機構側で回転方向とオフセットが自動で計算
                action.targetPos = moveDir * action.target;
            }
            else
            {
                action.targetPos = moveDir * (action.target * action.dir + action.offset);
            }
            action.targetPos /= Thousand;

            // カムポジ作成
            if (camPosInfos.Find(d => d.Target == action.target) == null)
            {
                camPosInfos.Add(new CamPosInfo
                {
                    Target = action.target,
                    Position = action.targetPos,
                    end = actionIo.end
                });
            }

            // 基本動作設定
            if (unitSetting.actionSetting.acl == 1)
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
                if (isRotate)
                {
                    // ★回転時は加速度は現状無効
                    action.aclVal = 0;
                    action.dclVal = 0;
                }
                else
                {
                    action.aclVal = action.acl * 9800;
                    action.dclVal = action.dcl * 9800;
                }
                // 解の公式における判別式の計算を行う。
                // 加速度・減速度が 0 は「加減速なし」（瞬時に速度が出る）なので、その側の項（1/加速度）は 0 にする。
                // （以前は 1/0=∞ で解なしになり、時間設定（加速・減速を半分ずつ）に切り替わって、一定速度で動かなかった）
                var invAcl = action.aclVal > 0 ? 1 / action.aclVal : 0f;
                var invDcl = action.dclVal > 0 ? 1 / action.dclVal : 0f;
                float a = -(invAcl + invDcl);
                float b = 2 * action.time;
                float c = -2 * Math.Abs(action.stroke);
                float discriminant = CommonFunction.Discriminant(a, b, c);

                if ((invAcl == 0f) && (invDcl == 0f))
                {
                    // 加減速なし：全体を一定速度（ストローク÷時間）で動かす。
                    // ★回転は上で加速度を無効化(aclVal=dclVal=0)しているため必ずここを通る。
                    //   以前は !isRotate で除外しており、その場合 a=0 の二次方程式で velocity=NaN となって
                    //   aclTime/dclTime=NaN → CreateCurve が全区間ストローク値（終端else）になり、
                    //   回転軸（および回転が主軸の拡張機構）が開始→終了へ一気に飛んで途中波形が出なかった。
                    action.velocity = action.time > 0 ? Math.Abs(action.stroke) / action.time : 0f;
                    action.aclTime = 0f;
                    action.dclTime = 0f;
                }
                else if (discriminant > 0)
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
                    if (!isRotate)
                    {
                        UnityEngine.Debug.LogWarning($"[動作] {unitSetting.name}「{action.startName}→{action.endName}」: 加速度 {action.acl}G / 減速度 {action.dcl}G では " +
                            $"{Math.Abs(action.stroke)}mm を {action.time * Thousand:0}ms で動けないため、加速 {action.aclTime:0}ms・減速 {action.dclTime:0}ms" +
                            $"（約 {action.aclVal / 9800f:0.00}G）で動かします");
                    }
                }
            }
        }

        // 回り続ける軸の判定と波形作成
        SetupLoopAxis();

        if (!GlobalScript.isSystemRecorder)
        {
            // 起動時OFF
            foreach (var campos in camPosInfos)
            {
                SetTagValue(campos.end, ref campos._EndOutput, 0);
            }
        }
    }

    /// <summary>
    /// 回り続ける軸（全部の動作が連続のバケット）の判定と、動作ごとの波形作成
    /// </summary>
    private void SetupLoopAxis()
    {
        var actions = unitSetting.actionSetting.actions;
        isLoopAxis = isBacket && !exModeChange && (actions.Count > 0);
        foreach (var action in actions)
        {
            if (!action.isContinue)
            {
                isLoopAxis = false;
                break;
            }
        }
        loopCurves.Clear();
        if (!isLoopAxis)
        {
            return;
        }
        loopCycle = unitSetting.actionSetting.cycle;
        if (loopCycle <= 0)
        {
            // サイクル未設定なら最後の動作の終わりまで
            foreach (var action in actions)
            {
                loopCycle = Math.Max(loopCycle, action.trg + (long)Math.Round(action.time * Thousand));
            }
        }
        loopStrokeSum = 0;
        var outside = 0;
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            // 向きは外部動作と同じく dir を掛けない（バケットは基本が正転。ストロークの符号だけ見る）
            var st = action.stroke < 0 ? -Math.Abs(action.stroke) : Math.Abs(action.stroke);
            var curve = new ActionCurveInfo();
            curve.CreateCurve(action, Vector3.zero, st, actionIos[i]);
            loopCurves.Add(curve);
            if (action.trg < loopCycle)
            {
                loopStrokeSum += st;
            }
            else
            {
                outside++;
            }
        }
        loopSw.Restart();
        loopPrvTravel = 0;
        loopPrvTime = 0;
        UnityEngine.Debug.Log($"[動作] {unitSetting.name}: 全部の動作が連続のため、サイクル時刻に合わせて回し続けます" +
            $"（サイクル {loopCycle}ms・動作 {actions.Count - outside}個・1サイクル {loopStrokeSum:0.#}mm" +
            (outside > 0 ? $"・trg がサイクル以上の {outside}個は使いません" : "") + "）");
    }

    /// <summary>
    /// 回り続ける軸の更新。サイクル時刻から、このサイクルで trg を過ぎた動作の進みを足して位置を決める
    /// （開始タグは待たない。ComInner と同じ時刻を使うので他の動作と同期する。時刻が戻れば位置も戻る）
    /// </summary>
    private void LoopAxisUpdate()
    {
        long time = GlobalScript.isSystemRecorder ? GlobalScript.sysRecMilliseconds : (cycleTag == null ? loopSw.ElapsedMilliseconds : GlobalScript.GetTagData(cycleTag));
        if (time < 0)
        {
            time = 0;
        }
        var cycleNo = time / loopCycle;
        var inCycle = time - cycleNo * loopCycle;
        var actions = unitSetting.actionSetting.actions;
        double travel = 0;
        var moving = false;
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action.trg >= loopCycle)
            {
                continue;
            }
            var t = inCycle - action.trg;
            if (t < 0)
            {
                continue;
            }
            var curve = loopCurves[i];
            float pos = 0;
            if (!curve.getPosition(t, ref pos))
            {
                moving = true;
            }
            travel += pos;
            // 通信遅れ時間込みで終了IOをON（動作の終わりの delay 前から）
            var count = curve.actCurve.Count;
            if ((t >= count - delayTime) && (t < count))
            {
                var campos = camPosInfos.Find(d => d.end == actionIos[i].end);
                if (campos != null)
                {
                    campos.AdvanceAngle = true;
                }
            }
        }
        // 周長で割った余りで渡す（長時間運転でも精度が落ちないよう double で計算）
        var total = cycleNo * loopStrokeSum + travel;
        var loopMm = BacketLoopMm;
        var posMm = total;
        if (loopMm > 0.001f)
        {
            posMm %= loopMm;
            if (posMm < 0)
            {
                posMm += loopMm;
            }
        }
        SetBacketPosition((float)posMm);

        // 表示用
        isMoving = moving;
        nowTime = inCycle;
        nowPos = (float)posMm;
        if (time != loopPrvTime)
        {
            nowSpd = (float)((total - loopPrvTravel) / (time - loopPrvTime) * 1000);
        }
        loopPrvTravel = total;
        loopPrvTime = time;

        // 出力処理（終了IOは動作の終わりだけ ON）
        if (!GlobalScript.isSystemRecorder)
        {
            foreach (var campos in camPosInfos)
            {
                SetTagValue(campos.end, ref campos._EndOutput, campos.AdvanceAngle ? 1 : 0);
                campos.AdvanceAngle = false;
            }
        }
    }

    /// <summary>FixedUpdate
    /// タイマー処理。
    /// 判定（開始・完了IO）は Tick にまとめ、外部通信のシリンダは通信スレッドから呼ぶ（描画が止まっても PLC への応答が遅れない）。
    /// ここでは通信スレッドが動いていない時だけ Tick を呼び、判定が決めた位置を見た目に反映する
    /// </summary>
    // Update is called once per frame
//    protected override void MyFixedUpdate()
    protected override void FixedUpdate()
    {
        if (moveObject == null)
        {
            return;
        }
        if (actionIos == null)
        {
            actionIos = new();
        }
        if ((actionIos.Count == 0) || (actionCurve.actCurve == null))// || (actionCurve.actionIo == null))
        {
            RenewMoveDir();
        }
        if (isLoopAxis)
        {
            // 回り続ける軸は開始タグを待たずにサイクル時刻で動かす
            LoopAxisUpdate();
            return;
        }
        EnsureComTicker();
        var now = Stopwatch.GetTimestamp();
        bool wasMoving;
        float pos;
        Vector3 position;
        lock (logicLock)
        {
            if (!IsComTickAlive(now))
            {
                // 内部通信・バケット・記録再生、または通信スレッドが動いていない時はここで判定する
                Tick(now, false);
            }
            wasMoving = tickWasMoving;
            pos = tickPos;
            position = innerPosition;
            TrackFrameGap(now);
        }

        // 見た目への反映
        try
        {
            if (exModeChange)
            {
                // 拡張機構モード変更時（ロード完了前はexScript未生成のためスキップ）
                if ((exScript != null) && (position != appliedPosition))
                {
                    appliedPosition = position;
                    exScript.SetExTarget(position);
                    ApplyChuckChildren(position);
                }
            }
            else if (isBacket)
            {
                // バケットは経路上の進みで動かす（判定もメインスレッド）
                if (wasMoving)
                {
                    MoveBacket(pos);
                    ApplyChuckChildren(position);
                }
            }
            else if (position != appliedPosition)
            {
                appliedPosition = position;
                if (isRotate)
                {
                    // 回転動作
                    moveObject.transform.localEulerAngles = position;
                }
                else
                {
                    // 直線動作
                    moveObject.transform.localPosition = position;
                }
                ApplyChuckChildren(position);
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.Log($"エラー：ユニット名「{unitSetting.name}」: {ex.Message}");
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

        // 前回データ保持
        prvTime = nowTime;
        prvPos = nowPos;
    }

    /// <summary>
    /// チャックの子ユニットを動かす
    /// </summary>
    private void ApplyChuckChildren(Vector3 position)
    {
        if (chuckSetting == null)
        {
            return;
        }
        if (isRotate)
        {
            // 回転動作
            foreach (var child in chuckSetting.children)
            {
                child.setting.moveObject.transform.localEulerAngles = (exModeChange ? position : moveObject.transform.localEulerAngles) * child.dir * child.rate + child.offset * moveDir;
            }
        }
        else
        {
            // 直線動作
            foreach (var child in chuckSetting.children)
            {
                child.setting.moveObject.transform.localPosition = (exModeChange ? position : moveObject.transform.localPosition) * child.dir * child.rate + child.offset * moveDir / Thousand;
            }
        }
    }

    /// <summary>
    /// 判定（開始・完了IO・位置）。logicLock の中で呼ぶ。
    /// メインスレッド（FixedUpdate）と通信スレッドのどちらから呼ばれても同じ処理（内部と外部で処理をそろえる）。
    /// 通信スレッドからは Unity の機能（Transform・タグの引き当て）を使わない
    /// </summary>
    /// <param name="now">呼んだ時刻（Stopwatch.GetTimestamp）</param>
    /// <param name="onComThread">通信スレッドからか</param>
    private void Tick(long now, bool onComThread)
    {
        tickWasMoving = isMoving;
        if (!isMoving)
        {
            // 経過時間クリア
            nowTime = 0;
            // トリガ更新
            for (int i = 0; i < actionIos.Count; i++)
            {
                actionIos[i].RenewTrigger(ReadTag(actionIos[i].start, ref actionIos[i]._StartInput, onComThread));
            }
            // 入力I/Oチェック
            for (int i = 0; i < actionIos.Count; i++)
            {
                if (actionIos[i].isTrigger)
                {
                    // 動作開始トリガ検出中（開始時刻＝この判定の時刻。通信スレッドなら受信の直後）
                    startTs = now;
                    // 目標座標と現在座標の距離をストロークとして動作曲線を作成する
                    if (!Generate_ST_Curve(i))
                    {
                        // 到達済み
                        continue;
                    }
                    isMoving = true;
                    BeginResponseLog(now);
                    break;
                }
            }
            timeOffset = GlobalScript.isSystemRecorder ? GlobalScript.sysRecMilliseconds : (cycleTag == null ? 0 : GlobalScript.GetTagData(cycleTag));
        }
        else
        {
            // 経過測定
            nowTime = (GlobalScript.isSystemRecorder ? GlobalScript.sysRecMilliseconds : (cycleTag == null ? (now - startTs) * 1000 / Stopwatch.Frequency : GlobalScript.GetTagData(cycleTag))) - timeOffset;
            // 通信遅れ時間込みで終了IOをON
            if (nowTime >= actionCurve.actCurve.Count - delayTime)
            {
                // 進角指令
                var campos = camPosInfos.Find(d => d.end == actionCurve.actionIo.end);
                if (campos != null)
                {
                    campos.AdvanceAngle = true;
                }
                if ((responseLog != null) && (responseLog.onTs == 0))
                {
                    responseLog.onTs = now;
                }
            }
            // 位置取得
            float pos = 0;
            var isEnd = actionCurve.getPosition(nowTime, ref pos);
            if (isEnd)
            {
                // 動作終了
                if (actionCurve.isContinue)
                {
                    // 継続動作ならインターロック
                    isMoving = ReadTag(actionCurve.actionIo.end, ref actionCurve.actionIo._StartInput, onComThread) != 0;
                }
                else
                {
                    isMoving = false;
                }
            }
            // バケットは innerPosition を使わない（経路上の進みで動かす。拡張機構モード変更時は使う）
            if (exModeChange || !isBacketLogic)
            {
                if (isEnd)
                {
                    innerPosition = isRotate ? actionCurve.targetPos * Thousand : actionCurve.targetPos;
                }
                else
                {
                    innerPosition = isRotate ? actionCurve.startPos * Thousand + pos * moveDir : actionCurve.startPos + pos * moveDir / Thousand;
                }
            }
            tickPos = pos;
        }

        // 出力処理
        if (!GlobalScript.isSystemRecorder)
        {
            foreach (var campos in camPosInfos)
            {
                float dist = 0;
                if (isRotate)
                {
                    dist = Vector3.Distance(campos.Position * Thousand, innerPosition) % 360;
                }
                else
                {
                    dist = Vector3.Distance(campos.Position, innerPosition) * Thousand;
                }
                WriteTag(campos.end, ref campos._EndOutput, campos.AdvanceAngle ? 1 : (dist <= 1 ? 1 : 0), onComThread);
                campos.AdvanceAngle = false;
            }
        }
        CheckResponseLog(now);
    }

    /// <summary>
    /// タグを読む。通信スレッドからは引き当て済みのタグの値だけを読む（引き当て・破棄の確認は Unity の機能のため）
    /// </summary>
    private int ReadTag(string tag, ref TagInfo tagInfo, bool onComThread)
    {
        if (!onComThread)
        {
            return GetTagValue(tag, ref tagInfo);
        }
        // KssBaseScript.GetTagValue の、引き当て済みの時と同じ読み方。
        // TagInfo は ScriptableObject なので、== null（Unity の比較）は使わず参照で比べる
        bool hasBit = TryParseBitTag(ref tag, out int bitNo);
        if (!ReferenceEquals(tagInfo, null))
        {
            if (hasBit)
            {
                return (tagInfo.Value >> bitNo) & 1;
            }
            return tagInfo.isFloat ? (int)(tagInfo.fValue * 1000000f) : tagInfo.Value;
        }
        // 数値指定（常に ON 等）
        return int.TryParse(tag, out var value) ? value : 0;
    }

    /// <summary>
    /// タグに書く。通信スレッドからは引き当て済みのタグにだけ書く
    /// </summary>
    private void WriteTag(string tag, ref TagInfo tagInfo, int value, bool onComThread)
    {
        if (!onComThread)
        {
            SetTagValue(tag, ref tagInfo, value);
            return;
        }
        // KssBaseScript.SetTagValue の、引き当て済みの時と同じ書き方
        if (ReferenceEquals(tagInfo, null))
        {
            return;
        }
        if (TryParseBitTag(ref tag, out int bitNo))
        {
            SetTagBit(tagInfo, bitNo, value == 1);
        }
        else
        {
            tagInfo.Value = value;
        }
    }

    /// <summary>
    /// 外部通信のシリンダなら、判定を通信スレッドに登録する（メインスレッド）。
    /// 回り続ける軸・バケット（経路上の進みを差分で積むため、描画と一緒に回す）・記録再生は登録しない
    /// </summary>
    private void EnsureComTicker()
    {
        if ((comTickerCom != null) || comTickerChecked || !GlobalScript.isLoaded)
        {
            return;
        }
        comTickerChecked = true;
        if (isLoopAxis || (isBacket && !exModeChange) || GlobalScript.isSystemRecorder)
        {
            return;
        }
        lock (logicLock)
        {
            // 通信スレッドからはタグを引き当てられないので、ここで済ませておく（完了IOは RenewMoveDir で引き当て済み）
            ComProtocolBase com = null;
            foreach (var io in actionIos)
            {
                GetTagValue(io.start, ref io._StartInput);
                if ((com == null) && (io._StartInput != null))
                {
                    com = ComProtocolBase.FindByName(io._StartInput.Database);
                }
            }
            if (com == null)
            {
                // 内部通信（ComInner）等。今まで通り FixedUpdate で判定する
                return;
            }
            comTicker ??= TickFromComThread;
            comTickerCom = com;
            lastComTickTs = 0;
            com.AddTicker(comTicker);
        }
    }

    /// <summary>
    /// 通信スレッドへの登録を外す
    /// </summary>
    private void RemoveComTicker()
    {
        if ((comTickerCom != null) && (comTicker != null))
        {
            comTickerCom.RemoveTicker(comTicker);
        }
        comTickerCom = null;
        comTickerChecked = false;
        lastComTickTs = 0;
    }

    /// <summary>
    /// 通信スレッドから呼ばれる判定
    /// </summary>
    private void TickFromComThread(long now)
    {
        lock (logicLock)
        {
            if (comTickerCom == null)
            {
                // 登録を外した後に呼ばれた
                return;
            }
            lastComTickTs = now;
            Tick(now, true);
        }
    }

    /// <summary>
    /// 通信スレッドが判定を回しているか（最後に回してから 0.5 秒以内）。
    /// 未接続で接続待ちが長い時などは FixedUpdate 側で判定する（同じ判定なのでどちらが回しても結果は同じ）
    /// </summary>
    private bool IsComTickAlive(long now)
    {
        return (comTickerCom != null) && (lastComTickTs != 0) && ((now - lastComTickTs) < Stopwatch.Frequency / 2);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        lock (logicLock)
        {
            RemoveComTicker();
        }
    }

    /// <summary>
    /// 応答遅れの計測開始（外部通信の開始IOで動き出した時。内部通信・記録再生では測らない）。logicLock の中で呼ぶ
    /// </summary>
    private void BeginResponseLog(long now)
    {
        responseLog = null;
        if (GlobalScript.isSystemRecorder)
        {
            return;
        }
        // 完了IOは判定と同じく、作った波形の動作IOで見る
        // 通信スレッドからも呼ぶので、Unity の == null は使わず参照で比べる
        var io = actionCurve.actionIo;
        if (io == null)
        {
            return;
        }
        var startCom = ReferenceEquals(io._StartInput, null) ? null : ComProtocolBase.FindByName(io._StartInput.Database);
        var campos = camPosInfos.Find(d => d.end == io.end);
        var endCom = ((campos == null) || ReferenceEquals(campos._EndOutput, null)) ? null : ComProtocolBase.FindByName(campos._EndOutput.Database);
        if ((startCom == null) || (endCom == null) || (startCom.lastRecvStartTs == 0))
        {
            return;
        }
        responseLog = new ResponseLog
        {
            startCom = startCom,
            endCom = endCom,
            recvTs = startCom.lastRecvStartTs,
            detectTs = now,
            expectedMs = Math.Max(0f, actionCurve.actCurve.Count - delayTime),
            start = io.start,
            end = io.end,
        };
    }

    /// <summary>
    /// FixedUpdate の間隔を記録する（応答遅れのログに出す）。logicLock の中で呼ぶ
    /// </summary>
    private void TrackFrameGap(long now)
    {
        var gapMs = prvFixedTs == 0 ? 0f : (float)((now - prvFixedTs) * 1000.0 / Stopwatch.Frequency);
        prvFixedTs = now;
        if (responseLog != null)
        {
            responseLog.maxGapMs = Math.Max(responseLog.maxGapMs, gapMs);
        }
    }

    /// <summary>
    /// 応答遅れの計測。完了IOを ON にした後の送信が終わったら、予定より遅れた分をログに出す。logicLock の中で呼ぶ
    /// </summary>
    private void CheckResponseLog(long now)
    {
        if ((responseLog == null) || (responseLog.onTs == 0))
        {
            return;
        }
        var com = responseLog.endCom;
        if (com.lastSendStartTs <= responseLog.onTs)
        {
            // まだ ON を送っていない（送信が無いまま 10 秒経ったらやめる）
            if ((now - responseLog.onTs) > Stopwatch.Frequency * 10)
            {
                responseLog = null;
            }
            return;
        }
        var log = responseLog;
        responseLog = null;
        double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        var totalMs = ToMs(com.lastSendDoneTs - log.recvTs);
        var lateMs = totalMs - log.expectedMs;
        if (lateMs < ResponseLogThresholdMs)
        {
            return;
        }
        var detectMs = ToMs(log.detectTs - log.recvTs);
        var onLateMs = ToMs(log.onTs - log.detectTs) - log.expectedMs;
        var sendMs = ToMs(com.lastSendDoneTs - log.onTs);
        UnityEngine.Debug.Log($"[応答遅れ] {unitSetting.name}: 開始IO「{log.start}」を受けてから完了IO「{log.end}」を送るまで {totalMs:0}ms" +
            $"（予定 {log.expectedMs:0}ms より {lateMs:0}ms 遅い。内訳：開始に気づくまで {detectMs:0}ms・完了ONの遅れ {onLateMs:0}ms・" +
            $"ONから送信完了まで {sendMs:0}ms・フレームの最大間隔 {log.maxGapMs:0}ms・判定 {(comTickerCom != null ? "通信スレッド" : "フレーム")}）");
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
                if (isBacketLogic)
                {
                    // バケットは周回するので、目標位置の差ではなく各動作のストローク（進む量）で動かす
                    st = Math.Abs(action.stroke);
                }
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
                if (isBacketLogic)
                {
                    // バケットは周回するので各動作のストロークで動かす。
                    // （目標位置の差で測ると、同じ目標が続く連続動作（例：毎回 30mm 進む）が 0 になり、
                    //   下の「動作なし」で波形の作成ごと止まって全く動かなかった）
                    st = Math.Abs(action.stroke);
                }
            }
            if (st <= 0.001)
            {
                // 0.1m以下は動作なし
                return false;
            }
            var direction = (action.targetPos - startPos).normalized;
            if (isBacketLogic)
            {
                // バケットの向きは外部動作と同じく dir を掛けない（基本が正転。ストロークの符号だけ見る）。
                // 目標位置は dir 込みなので、dir=-1 だと値が減り続け、MoveBacket が毎フレーム「折り返し」と
                // 判定して位置を積み上げてしまう（逆向きの判定が一度も成立しない）
                if (action.stroke < 0)
                {
                    st = -st;
                }
            }
            else if (direction.x + direction.y + direction.z < 0)
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
                actionCurve.offset = exModeChange ? 0 : action.offset;
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
            // 完了時に getPosition が返す最終位置(st)を複合カーブの終端値に合わせる
            // ※未設定(0)のままだと完了フレームで pos=0 が返り、バケット軸は最終サンプル〜終端の移動量を毎サイクル取りこぼして徐々にずれる
            actionCurve.st = actionCurve.actCurve[actionCurve.actCurve.Count - 1];
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
