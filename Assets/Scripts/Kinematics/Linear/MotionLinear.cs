using NUnit.Framework;
using Org.BouncyCastle.Asn1.BC;
using Org.BouncyCastle.Ocsp;
using Parameters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.AI;
using static MotionLinear;
using static Parameters.LinearSetting;
using static Unity.Burst.Intrinsics.X86.Sse4_2;
using static UnityEngine.Rendering.DebugUI;

public class MotionLinear : AxisMotionBase
{
    #region 列挙型定義
    /// <summary>
    /// ポイントタイプ
    /// </summary>
    public enum PointType
    {
        PP,
        BUFF,
        TP,
        TPE,
        MTP,
        MTPE
    }
    #endregion 列挙型定義

    #region クラス定義
    /// <summary>
    /// 動作ポイント
    /// </summary>
    public struct MotionPoint
    {
        public long time;       // 時間(ms)
        public float position;  // 位置
        public float velocity;  // 速度
        public float accel;     // 加速度
        public float jerk;      // ジャーク
    }

    /// <summary>
    /// 動作テーブル
    /// </summary>
    public class MotionTable
    {
        /// <summary>
        /// 動作中
        /// </summary>
        public bool act = false;
        /// <summary>
        /// 動作完了
        /// </summary>
        public bool fin = false;
        /// <summary>
        /// 衝突中
        /// </summary>
        public bool collision = false;
        /// <summary>
        /// 処理待ち
        /// </summary>
        public bool processwait = false;
        /// <summary>
        /// 処理中
        /// </summary>
        public bool processing = false;
        /// <summary>
        /// 処理完了
        /// </summary>
        public bool processed = false;
        /// <summary>
        /// 経過時間
        /// </summary>
        public long laps = 0;
        /// <summary>
        /// 時間オフセット
        /// </summary>
        public long lapsOffset = 0;
        /// <summary>
        /// 目標位置
        /// </summary>
        public float target = 0;
        /// <summary>
        /// 初期速度
        /// </summary>
        public float vf = 0;
        /// <summary>
        /// 最終速度
        /// </summary>
        public float ve = 0;
        /// <summary>
        /// 動作開始前位置
        /// </summary>
        public float start = 0;
        /// <summary>
        /// 動作位置
        /// </summary>
        public float pos = 0;
        /// <summary>
        /// 現在速度
        /// </summary>
        public float spd = 0;
        /// <summary>
        /// 定速移動
        /// </summary>
        public bool isEvenSpd;
        /// <summary>
        /// 動作テーブル
        /// </summary>
        public List<MotionPoint> table = new();
    }

    /// <summary>
    /// ムーバー情報
    /// </summary>
    public class MoverInfo : IDisposable
    {
        /// <summary>
        /// ムーバー状態
        /// </summary>
        public enum MoverStatus
        {
            None,
            Acl,
            Even,
            Dcl,
            ProcessWait,
            Processing,
            Processed,
            Collision
        }
        /// <summary>
        /// ID
        /// </summary>
        public int id;
        /// <summary>
        /// オブジェクト
        /// </summary>
        public GameObject obj;
        /// <summary>
        /// ステータス表示用オブジェクト
        /// </summary>
        private GameObject statObj;
        /// <summary>
        /// ステータス表示用ステータス
        /// </summary>
        private Material statMat;
        /// <summary>
        /// 動作中ポイント番号
        /// </summary>
        public int pointno = -1;
        /// <summary>
        /// 現在位置
        /// </summary>
        public float pos = 0;
        /// <summary>
        /// ムーバーサイズ
        /// </summary>
        public float size = 50f;
        /// <summary>
        /// リニア長
        /// </summary>
        public float length = 50f;
        /// <summary>
        /// 後ろのムーバー
        /// </summary>
        public MoverInfo next;
        /// <summary>
        /// 前のムーバー
        /// </summary>
        public MoverInfo prv;
        /// <summary>
        /// モーションテーブル
        /// </summary>
        public MotionTable motion = new();
        /// <summary>
        /// サイクル
        /// </summary>
        public long cycle = 0;
        /// <summary>
        /// サイクル
        /// </summary>
        public long prvCycle = 0;
        /// <summary>
        /// 加速完了時間
        /// </summary>
        private int T1;
        /// <summary>
        /// 定速完了時間
        /// </summary>
        private int T2;
        /// <summary>
        /// 処理時間
        /// </summary>
        public int processTime;
        /// <summary>
        /// 処理時間
        /// </summary>
        public int processTimeMax;
        /// <summary>
        /// 前回からの時間
        /// </summary>
        public long laps
        {
            get
            {
                return cycle - prvCycle;
            }
        }
        /// <summary>
        /// 先頭フラグ
        /// </summary>
        public bool isHead
        {
            get
            {
                return id == 0;
            }
        }
        /// <summary>
        /// ムーバー状態
        /// </summary>
        public MoverStatus status
        {
            get
            {
                if (motion.act)
                {
                    // 動作中
                    if (motion.collision)
                    {
                        return MoverStatus.Collision;
                    }
                    else
                    {
                        if (motion.laps < T1)
                        {
                            return MoverStatus.Acl;
                        }
                        else if (motion.laps < T2)
                        {
                            return MoverStatus.Even;
                        }
                        else
                        {
                            return MoverStatus.Dcl;
                        }
                    }
                }
                else if (motion.processwait)
                {
                    return MoverStatus.ProcessWait;
                }
                else if (motion.processing)
                {
                    return MoverStatus.Processing;
                }
                else if (motion.processed)
                {
                    return MoverStatus.Processed;
                }
                else
                {
                    return MoverStatus.None;
                }
            }
        }
        /// <summary>
        /// ムーバー状態
        /// </summary>
        public string txtStatus
        {
            get
            {
                if (motion.act)
                {
                    // 動作中
                    if (motion.collision)
                    {
                        return "衝突中";
                    }
                    else
                    {
                        if (motion.laps < T1)
                        {
                            return "加速中";
                        }
                        else if (motion.laps < T2)
                        {
                            return "定速中";
                        }
                        else
                        {
                            return "減速中";
                        }
                    }
                }
                else if (motion.processwait)
                {
                    return "処理待ち";
                }
                else if (motion.processing)
                {
                    return "処理中";
                }
                else if (motion.processed)
                {
                    return "処理完了";
                }
                else
                {
                    return "待機中";
                }
            }
        }
        /// <summary>
        /// 処理時間状態
        /// </summary>
        public string txtPos
        {
            get
            {
                if (motion.act)
                {
                    return $"{motion.pos.ToString("0.000")} / {motion.target.ToString("0.000")}";
                }
                else
                {
                    return motion.pos.ToString("0.000");
                }
            }
        }
        /// <summary>
        /// 処理時間状態
        /// </summary>
        public string txtProcessTime
        {
            get
            {
                var ret = "---";
                if (motion.processing)
                {
                    ret = $"{processTime} / {processTimeMax}";
                }
                return ret;
            }
        }
        /// <summary>
        /// ステータス作成
        /// </summary>
        public void CreateStatus(Vector3 pos)
        {
            statObj = Instantiate((GameObject)Resources.Load("3DModel/Works/Sphere"), obj.transform);
            statObj.transform.localPosition = pos;
            statMat = statObj.GetComponent<MeshRenderer>().material;
        }
        /// <summary>
        /// ステータスセット
        /// </summary>
        public void SetStatus()
        {
            if (statMat != null)
            {
                var stat = status;
                if (stat == MoverStatus.Acl)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.yellow);
                }
                else if (stat == MoverStatus.Even)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.green);
                }
                else if (stat == MoverStatus.Dcl)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.orange);
                }
                else if (stat == MoverStatus.ProcessWait)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.purple);
                }
                else if (stat == MoverStatus.Processing)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.aquamarine);
                }
                else if (stat == MoverStatus.Processed)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.blue);
                }
                else if (stat == MoverStatus.Collision)
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.red);
                }
                else
                {
                    statMat.SetColor("_BaseColor", UnityEngine.Color.gray);
                }
            }
        }
        /// <summary>
        /// モーションセット
        /// </summary>
        public void SetMotion(float target, float dist, SpeedInfo spdInfo)
        {
            motion.pos = 0;
            motion.target = target;
            motion.vf = motion.spd;
            motion.ve = spdInfo.ve;
            motion.start = pos;
            motion.table = GenerateProfile(dist, motion.vf, spdInfo.ve, spdInfo.vm, spdInfo.acl, spdInfo.dcl, ref T1, ref T2);
            motion.act = true;
            motion.fin = false;
            motion.collision = false;
            motion.isEvenSpd = (spdInfo.vf == spdInfo.vm) && (spdInfo.vf == spdInfo.ve);
            motion.lapsOffset = cycle;
        }
        /// <summary>
        /// 位置を更新
        /// </summary>
        public float RenewPosition()
        {
            var ret = pos;
            if (motion.act)
            {
                // 経過測定
                motion.laps = cycle - motion.lapsOffset;
                if (GetPosition(motion.laps, ref motion.pos, ref motion.spd))
                {
                    motion.act = false;
                    motion.fin = true;
                    if (motion.isEvenSpd)
                    {
                        pos += motion.ve * laps / 1000f;
                    }
                    else
                    {
                        pos = motion.pos;
                    }
                    ret = pos;
                }
                else
                {
                    ret = motion.pos + motion.start;
                }
                var dist = Mathf.Abs(prv.pos - ret + length) % length;
                if (dist <= size - 0.000001)
                {
                    // 1μm以上で接触
                    ret = ((prv.pos - size) + length) % length;
                    motion.pos = ((ret - motion.start) + length) % length;
                    var index = motion.table.FindIndex(d => d.position >= motion.pos);
                    if (index >= 0)
                    {
                        var t = motion.table[index];
                        motion.lapsOffset += (motion.laps - t.time);
                        // 動作中に戻す
                        motion.act = true;
                        motion.fin = false;
                    }
                    motion.collision = true;
                }
                else
                {
                    motion.collision = false;
                }
            }
            else if (motion.isEvenSpd)
            {
                // 定速移動
                ret += motion.ve * laps / 1000f;
            }
            SetStatus();
            prvCycle = cycle;
            return ret;
        }
        /// <summary>
        /// テーブル位置取得
        /// </summary>
        /// <param name="time"></param>
        /// <param name="pos"></param>
        /// <returns></returns>
        public bool GetPosition(float time, ref float nextpos, ref float nextspd)
        {
            if (time >= motion.table.Count - 1)
            {
                nextpos = motion.target;
                nextspd = motion.ve;
                return true;
            }
            else if (time <= 0)
            {
                nextpos = 0;
                nextspd = motion.vf;
            }
            else
            {
                int t1 = (int)Math.Floor(time);
                float t2 = time - t1;
                float d1 = motion.table[t1].position;
                float d2 = motion.table[t1 + 1].position;
                float s1 = motion.table[t1].velocity;
                float s2 = motion.table[t1 + 1].velocity;
                nextpos = (d2 - d1) * t2 + d1;
                nextspd = (s2 - s1) * t2 + s1;
            }
            return false;
        }
        /// <summary>
        /// 削除時
        /// </summary>
        public void Dispose()
        {
            Destroy(statMat);
            Destroy(statObj);
        }
    }

    /// <summary>
    /// 停止ポイント
    /// </summary>
    public class StopPoint
    {
        /// <summary>
        /// ポイントID
        /// </summary>
        public int pointId;
        /// <summary>
        /// 停止ポイントID
        /// </summary>
        public int stopId;
        /// <summary>
        /// プロセスポイント
        /// </summary>
        public bool isPP;
        /// <summary>
        /// 通過開始ポイント
        /// </summary>
        public bool isTP;
        /// <summary>
        /// 通過開始ポイント
        /// </summary>
        public bool isMTP;
        /// <summary>
        /// 通過終了ポイント
        /// </summary>
        public bool isTPE;
        /// <summary>
        /// 通過終了ポイント
        /// </summary>
        public bool isMTPE;
#nullable enable
        /// <summary>
        /// 割当ムーバー
        /// </summary>
        public MoverInfo? mover = null;
        /// <summary>
        /// 通過ポイント
        /// </summary>
        public StopPoint? tp = null;
        /// <summary>
        /// 通過ポイント
        /// </summary>
        public StopPoint? mtp = null;
#nullable disable
        /// <summary>
        /// 通過終了ポイントへ移動中
        /// </summary>
        public bool isMoveToTPE = false;
        /// <summary>
        /// 通過終了ポイントへ移動中
        /// </summary>
        public bool isMoveToMTPE = false;
        /// <summary>
        /// 目標位置
        /// </summary>
        public float target;
        /// <summary>
        /// 次のポイント
        /// </summary>
        public StopPoint next;
        /// <summary>
        /// ムーバー動作可能
        /// </summary>
        public bool isReady
        {
            get
            {
                return mover != null && !mover.motion.act && mover.motion.fin;
            }
        }
        /// <summary>
        /// 動作中
        /// </summary>
        public bool isMoving
        {
            get
            {
                return mover != null && mover.motion.act;
            }
        }
    }

    /// <summary>
    /// ポイント情報
    /// </summary>
    public class PointInfo
    {
        /// <summary>
        /// ID
        /// </summary>
        public int id;
        /// <summary>
        /// 名前
        /// </summary>
        public string name;
        /// <summary>
        /// 初期復帰位置
        /// </summary>
        public bool isInit;
        /// <summary>
        /// ループ長
        /// </summary>
        public float totalLength;
        /// <summary>
        /// 設定位置
        /// </summary>
        public float pos;
        /// <summary>
        /// ポイントタイプ
        /// </summary>
        public PointType type;
        /// <summary>
        /// 動作タグ
        /// </summary>
        public string actTag;
        /// <summary>
        /// 処理開始タグ
        /// </summary>
        public string processTag;
        /// <summary>
        /// 完了タグ
        /// </summary>
        public string finTag;
        /// <summary>
        /// 処理時間
        /// </summary>
        public int processTime;
        /// <summary>
        /// 停止可能位置
        /// </summary>
        public List<StopPoint> stopPoints = new();
        /// <summary>
        /// 次のプロセスポイント
        /// </summary>
        public PointInfo nextPP;
        /// <summary>
        /// 次のマルチ通過ポイント
        /// </summary>
        public PointInfo nextMTP;
        /// <summary>
        /// 次のポイント
        /// </summary>
        public PointInfo next;
        /// <summary>
        /// 前のポイント
        /// </summary>
        public PointInfo prv;
        /// <summary>
        /// 次の停止可能位置
        /// </summary>
        public StopPoint nextStopPoint;
        /// <summary>
        /// 速度情報
        /// </summary>
        public SpeedInfo spdInfo;
        /// <summary>
        /// 経過時間
        /// </summary>
        public long laps = 0;
        /// <summary>
        /// 時間オフセット
        /// </summary>
        public long lapsOffset = 0;
        /// <summary>
        /// 時間
        /// </summary>
        public long cycle = 0;
        /// <summary>
        /// 処理中か
        /// </summary>
        public bool isProcess = false;
        /// <summary>
        /// 処理完了
        /// </summary>
        public bool isProcessed = false;
        /// <summary>
        /// サイクルラプス
        /// </summary>
        public long cycleLaps = 0;
        /// <summary>
        /// サイクルタイム
        /// </summary>
        public long cycleTime = 0;
        /// <summary>
        /// サイクルタイム(履歴)
        /// </summary>
        public List<long> cycleTimes = new List<long>();
        /// <summary>
        /// 空か？
        /// </summary>
        public bool isBlank
        {
            get
            {
                return restCount == stopPoints.Count;
            }
        }
        /// <summary>
        /// 通過開始ポイントか？
        /// </summary>
        public bool isTP
        {
            get
            {
                return type == PointType.TP;
            }
        }
        /// <summary>
        /// 通過開始ポイントか？
        /// </summary>
        public bool isMTP
        {
            get
            {
                return type == PointType.MTP;
            }
        }
        /// <summary>
        /// 通過終了ポイントか？
        /// </summary>
        public bool isTPE
        {
            get
            {
                return type == PointType.TPE;
            }
        }
        /// <summary>
        /// 通過終了ポイントか？
        /// </summary>
        public bool isMTPE
        {
            get
            {
                return type == PointType.MTPE;
            }
        }
        /// <summary>
        /// 通過開始ポイント完了
        /// </summary>
        public StopPoint tpFinPoint
        {
            get
            {
                return stopPoints.Find(sp => !sp.isMoveToTPE && (sp.mover != null) && sp.mover.motion.fin);
            }
        }
        /// <summary>
        /// 通過終了ポイント完了
        /// </summary>
        public StopPoint tpeFinPoint
        {
            get
            {
                return stopPoints.Find(sp => (sp.mover != null) && sp.mover.motion.fin);
            }
        }
        /// <summary>
        /// 通過開始ポイント完了
        /// </summary>
        public StopPoint mtpFinPoint
        {
            get
            {
                return stopPoints.Find(sp => !sp.isMoveToMTPE && (sp.mover != null) && sp.mover.motion.fin);
            }
        }
        /// <summary>
        /// 通過終了ポイント完了
        /// </summary>
        public StopPoint mtpeFinPoint
        {
            get
            {
                return stopPoints.Find(sp => (sp.mover != null) && sp.mover.motion.fin);
            }
        }
        /// <summary>
        /// 動作中か
        /// </summary>
        public bool isAct
        {
            get
            {
                var ret = false;
                foreach (var sp in stopPoints)
                {
                    if (sp.mover != null)
                    {
                        ret |= sp.mover.motion.act;
                    }
                }
                return ret;
            }
        }
        /// <summary>
        /// 動作完了か
        /// </summary>
        public bool isFin
        {
            get
            {
                var ret = true;
                foreach (var sp in stopPoints)
                {
                    if (sp.mover == null)
                    {
                        return false;
                    }
                    ret &= sp.mover.motion.fin;
                }
                return ret;
            }
        }
        /// <summary>
        /// 処理可能か
        /// </summary>
        public bool isEnableProcess
        {
            get
            {
                var ret = true;
                foreach (var sp in stopPoints)
                {
                    if (sp.mover == null)
                    {
                        return false;
                    }
                    ret &= sp.mover.motion.fin && !sp.mover.motion.processed;
                }
                return ret;
            }
        }
        /// <summary>
        /// 全ムーバー処理完了状態
        /// </summary>
        public bool isAllProcessed
        {
            get
            {
                var ret = true;
                foreach (var sp in stopPoints)
                {
                    if (sp.mover == null)
                    {
                        return false;
                    }
                    ret &= sp.mover.motion.fin && sp.mover.motion.processed;
                }
                return ret;
            }
        }
        /// <summary>
        /// 動作可能か
        /// </summary>
        public bool isEnableMove
        {
            get
            {
                var ret = false;
                if (type == PointType.PP)
                {
                    foreach (var sp in stopPoints)
                    {
                        if (sp.mover == null)
                        {
                            continue;
                        }
                        ret |= sp.mover.motion.fin;
                    }
                }
                return ret;
            }
        }
        /// <summary>
        /// 受け入れ可能残り数
        /// </summary>
        public int restCount
        {
            get
            {
                return stopPoints.Count(d => d.mover == null);
            }
        }
        /// <summary>
        /// 受け入れ可能ムーバー
        /// </summary>
        public StopPoint nextMover
        {
            get
            {
                return stopPoints.FirstOrDefault(d => d.mover == null);
            }
        }
        /// <summary>
        /// サイクル表示
        /// </summary>
        public string txtCycle
        {
            get
            {
                var ret = "---";
                if ((type == PointType.PP) && (cycleTimes.Count > 0))
                {
                    ret = $"{cycleTime} / {(int)cycleTimes.Average()}";
                }
                return ret;
            }
        }
        /// <summary>
        /// ムーバーをセットする
        /// </summary>
        /// <param name="mover"></param>
        public void SetMover(MoverInfo mover)
        {
            var next = nextMover;
            if (next != null)
            {
                var dist = (next.target + totalLength - mover.pos) % totalLength;
                next.mover = mover;
                mover.SetMotion(next.target, dist, spdInfo);
                mover.motion.processed = true;
            }
        }
        /// <summary>
        /// ムーバーをセットする
        /// </summary>
        /// <param name="mover"></param>
        public void SetMover(StopPoint nowPoint, StopPoint nextPoint)
        {
            var mover = nowPoint.mover;
            var next = nextPoint == null ? nextMover : nextPoint;
            if (next != null)
            {
                var dist = (next.target + totalLength - mover.pos) % totalLength;
                next.mover = mover;
                mover.SetMotion(next.target, dist, spdInfo);
                if (next.isPP)
                {
                    if (nowPoint.pointId != nextPoint.pointId)
                    {
                        mover.motion.processed = false;
                    }
                }
                else
                {
                    mover.motion.processed = true;
                }
            }
        }

        /// <summary>
        /// 処理更新
        /// </summary>
        public void RenewProcess(long cycle, bool processTag)
        {
            this.cycle = cycle;
            if (type == PointType.PP)
            {
                // プロセスポイントの場合
                if (isEnableProcess)
                {
                    // 処理可能状態
                    if (!isProcess)
                    {
                        //　動作完了初回
                        if (processTag)
                        {
                            // 処理開始
                            isProcess = true;
                            lapsOffset = cycle;
                            cycleTime = cycle - cycleLaps;
                            if (cycleTimes.Count >= 10)
                            {
                                cycleTimes.RemoveAt(0);
                            }
                            cycleTimes.Add(cycleTime);
                            cycleLaps = cycle;
                        }
                        // 処理待ち状態セット
                        foreach (var sp in stopPoints)
                        {
                            sp.mover.motion.processwait = !processTag;
                            sp.mover.processTime = 0;
                            sp.mover.processTimeMax = processTime;
                        }
                        isProcessed = false;
                    }
                    else
                    {
                        var time = (int)(cycle - lapsOffset);
                        isProcess = (processTime > cycle - lapsOffset);
                        // 処理完了
                        foreach (var sp in stopPoints)
                        {
                            sp.mover.motion.processing = isProcess;
                            sp.mover.motion.processed = !isProcess;
                            sp.mover.processTime = time;
                        }
                        isProcessed = !isProcess;
                    }
                }
                else
                {
                    isProcessed = false;
                }
            }
            else if (type == PointType.TP)
            {
                isProcessed = tpFinPoint != null;
            }
            else if (type == PointType.TPE)
            {
                isProcessed = tpeFinPoint != null;
            }
            else if (type == PointType.MTP)
            {
                isProcessed = mtpFinPoint != null;
            }
            else if (type == PointType.MTPE)
            {
                isProcessed = mtpeFinPoint != null;
            }
            else
            {
                isProcessed = true;
            }
        }
    }

    /// <summary>
    /// 速度情報
    /// </summary>
    public class SpeedInfo
    {
        public float vm;
        public float vf;
        public float ve;
        public float acl;
        public float dcl;
        public float jerkA;
        public float jerkD;
    }

    /// <summary>
    /// タグ情報
    /// </summary>
    protected  class TagStatus
    {
        public TagInfo tag;
        public int data;
        public bool value
        {
            get
            {
                return data == 1;
            }
        }
    }
    #endregion クラス定義

    #region メンバー定義
    /// <summary>
    /// バケット情報
    /// </summary>
    public List<MoverInfo> movers = new List<MoverInfo>();

    /// <summary>
    /// ポイント情報
    /// </summary>
    public List<PointInfo> points = new List<PointInfo>();

    /// <summary>
    /// タグ情報
    /// </summary>
    protected Dictionary<string, TagStatus> tags = new Dictionary<string, TagStatus>();

    /// <summary>
    /// 流れ方向
    /// </summary>
    private int dirL;

    // 高さ方向
    private int dirH;

    /// <summary>
    /// 奥行方向
    /// </summary>
    private int dirD;

    /// <summary>
    /// 計算長
    /// </summary>
    private float calcLength;

    /// <summary>
    /// 周長
    /// </summary>
    private float totalLength;

    /// <summary>
    /// 直線距離
    /// </summary>
    private float straightLength;

    /// <summary>
    /// カーブ距離(理論上)
    /// </summary>
    private float curveLength;

    /// <summary>
    /// カーブ距離
    /// </summary>
    private float calcCurveLength;

    /// <summary>
    /// ムーバーピッチ
    /// </summary>
    private float moverPitch;

    /// <summary>
    /// 初期位置オフセット
    /// </summary>
    private float moverOffsetPos;

    /// <summary>
    /// 高さ方向オフセット
    /// </summary>
    private float moverOffsetH;

    /// <summary>
    /// 初期角度オフセット
    /// </summary>
    private Vector3 moverOffsetAng;
    
    /// <summary>
    /// モデル中心
    /// </summary>
    private Vector3 center;

    /// <summary>
    /// ムーバーから一番近い点
    /// </summary>
    private VerticeInfo near;

    /// <summary>
    /// タイマー
    /// </summary>
    public System.Diagnostics.Stopwatch sw = new();

    /// <summary>
    /// 前回サイクル
    /// </summary>
    private long prvCycle;

    /// <summary>
    /// 衝突検知用に決まった時間以上は分割処理
    /// </summary>
    private const long cycleMax = 30;
    #endregion メンバー定義

    #region 関数定義
    /// <summary>
    /// サイクル処理
    /// </summary>
    protected override void FixedUpdate()
    {
        // サイクルタグ設定
        var tag = GlobalScript.callbackTags.Find(d => d.database == unitSetting.Database);
        cycleTag = cycleTag != null ? cycleTag : tag == null ? null : (tag.cycle.Tag == "" ? null : tag.cycle);
        // タグ更新
        foreach (var t in tags)
        {
            t.Value.data = GetTagValue(t.Key, ref t.Value.tag);
        }
        var cycle = cycleTag == null ? sw.ElapsedMilliseconds : GlobalScript.GetTagData(cycleTag);
        var difCycle = cycle - prvCycle;
        // 時間分割処理
        for (long c = 0; c < difCycle; c += cycleMax)
        {
            if (c == 0)
            {
                // 初回
                c = cycleMax;
            }
            if (c > difCycle)
            {
                c = difCycle;
            }
            // ポイント更新
            foreach (var point in points)
            {
                // ポイント処理
                point.RenewProcess(cycle, ((point.processTag == null) || (point.processTag == "")) ? true : tags[point.processTag].value);
                // ポイント判定)
                if (!point.isBlank && (point.isProcessed || point.isEnableMove))
                {
                    var isPP = point.nextPP == null ? false : tags.ContainsKey(point.nextPP.actTag) ? tags[point.nextPP.actTag].value : true;
                    if (point.isTP)
                    {
                        var sp = point.tpFinPoint;
                        // 通過中保持
                        point.nextPP.nextMover.tp = sp;
                        // 通過終了ポイントへ
                        point.nextPP.SetMover(sp, point.nextPP.nextMover);
                        sp.isMoveToTPE = true;
                    }
                    else if (point.isTPE)
                    {
                        // 次のポイントへ
                        StopPoint target = null;
                        var sp = point.tpeFinPoint;
                        for (var p = sp.next; p.pointId != point.nextPP.next.id; p = p.next)
                        {
                            if (sp.pointId != p.pointId)
                            {
                                if (p.mover == null)
                                {
                                    target = p;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        if (target != null)
                        {
                            // 目標位置移動可能
                            var np = points.Find(d => d.id == target.pointId);
                            np.SetMover(sp, target);
                            sp.mover = null;
                            sp.tp.mover = null;
                            sp.tp.isMoveToTPE = false;
                            sp.tp = null;
                        }
                    }
                    else if (point.isMTP)
                    {
                        var sp = point.mtpFinPoint;
                        // 通過中保持
                        point.nextPP.nextMover.mtp = sp;
                        // 通過終了ポイントへ
                        point.nextPP.SetMover(sp, point.nextPP.nextMover);
                        sp.isMoveToMTPE = true;
                    }
                    else if (point.isMTPE)
                    {
                        // 次のポイントへ
                        StopPoint target = null;
                        var sp = point.mtpeFinPoint;
                        for (var p = sp.next; p.pointId != point.nextPP.next.id; p = p.next)
                        {
                            if ((sp.pointId != p.pointId) && !p.isMTP && !p.isMTPE)
                            {
                                if (p.mover == null)
                                {
                                    target = p;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        if (target != null)
                        {
                            // 自分の前のムーバーの目標座標より前なら無効
                            if (sp.mtp.mover.prv.motion.target < target.target)
                            {
                            }
                            else
                            {
                                // 目標位置移動可能
                                var np = points.Find(d => d.id == target.pointId);
                                np.SetMover(sp, target);
                                sp.mover = null;
                                sp.mtp.mover = null;
                                sp.mtp.isMoveToMTPE = false;
                                sp.mtp = null;
                            }
                        }
                    }
                    else
                    {
                        foreach (var sp in point.stopPoints)
                        {
                            // 動作可能
                            StopPoint target = null;
                            int endId = -1;
                            if (sp.isReady)
                            {
                                // 検索終了ID
                                endId = sp.mover.motion.processed ? (isPP ? point.nextPP.next.id : point.nextPP.id) : point.next.id;
                            }
                            else if (sp.isMoving)
                            {
                                // 動作中
                                endId = !sp.mover.motion.processed ? (isPP ? point.nextPP.next.id : point.nextPP.id) : point.next.id;
                            }
                            if (endId >= 0)
                            {
                                for (var p = sp.next; p.pointId != endId; p = p.next)
                                {
                                    if (p.mover == null)
                                    {
                                        var count = 1;
                                        if (p.isMTP)
                                        {
                                            // 複数通過の場合は前の通過が使えるかチェック
                                            for (var m = p.next.next; m.isMTP; m = m.next.next)
                                            {
                                                if (p.next.isMTPE)
                                                {
                                                    if (m.mover == null)
                                                    {
                                                        p = m;
                                                    }
                                                    else
                                                    {
                                                        // 既に動作中の通過ムーバー
                                                        count++;
                                                    }
                                                }
                                            }
                                        }
                                        // 通過後のポイントがあるかチェック
                                        if (p.isMTP || p.isTP)
                                        {
                                            // 前のポイントに空きがあるかチェック
                                            var endPPId = point.nextPP.nextPP.nextPP.next.id;
                                            StopPoint esp = null;
                                            var start = false;
                                            for (var e = p.next; e.pointId != endPPId; e = e.next)
                                            {
                                                if (!e.isTP && !e.isTPE && !e.isMTP && !e.isMTPE && (e.mover == null))
                                                {
                                                    // 次のポイントがあるので動作可能
                                                    count--;
                                                    if (count == 0)
                                                    {
                                                        esp = p;
                                                        break;
                                                    }
                                                    start = true;
                                                }
                                                else if (start)
                                                {
                                                    break;
                                                }
                                            }
                                            if (p.isMTP)
                                            {
                                                target = esp;
                                                break;
                                            }
                                            else
                                            {
                                                p = esp;
                                            }
                                        }
                                        target = p;
                                    }
                                    else
                                    {
                                        if (!p.isTP && !p.isTPE && !p.isMTP && !p.isMTPE)
                                        {
                                            break;
                                        }
                                        else
                                        {
                                        }
                                    }
                                }
                                if (target != null)
                                {
                                    // 目標位置移動可能
                                    var np = points.Find(d => d.id == target.pointId);
                                    np.SetMover(sp, target);
                                    sp.mover = null;
                                }
                            }
                        }
                    }
                }
            }
            // ポイント出力更新
            if (!GlobalScript.isSystemRecorder)
            {
                foreach (var point in points)
                {
                    if ((point.type == PointType.PP) && (point.finTag != ""))
                    {
                        SetTagValue(point.finTag, ref tags[point.finTag].tag, point.isAllProcessed ? 1 : 0);
                    }
                }
            }
            // ムーバー位置更新
            foreach (var mover in movers)
            {
                mover.cycle = prvCycle + c;
                mover.pos = mover.RenewPosition() % totalLength;
                // 表示は最終データのみ
                if (c == difCycle)
                {
                    RenewMover(mover);
                }
            }
        }
        prvCycle = cycle;
    }

    /// <summary>
    /// 削除時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        foreach (var mover in movers)
        {
            mover.Dispose();
            Destroy(mover.obj);
        }
    }

    /// <summary>
    /// ムーバーをの位置を更新
    /// </summary>
    /// <param name="mover"></param>
    private void RenewMover(MoverInfo mover)
    {
        GetPositionOnPath(mover.pos, out Vector3 pos, out Vector3 dir, out Quaternion rot);
        mover.obj.transform.parent = moveObject.transform.parent;
        mover.obj.transform.localPosition = pos;
        if (dir != Vector3.zero)
        {
            if (dirL == 0)
            {
                if (dirH == 1)
                {
                    mover.obj.transform.localRotation = rot * Quaternion.Euler(moverOffsetAng);
                }
                else
                {

                    mover.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                    mover.obj.transform.localEulerAngles -= moverOffsetAng;
                }
            }
        }
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    protected override void PreModelRestruct()
    {
        base.PreModelRestruct();

        if ((linearSetting != null) && (linearSetting.gameObject != null))
        {
            // リニアパス作成
            CreateLinearPathPoints();
            
            // ムーバー作成
            CreateMoverObject();

            // ポイント作成
            CreatePointInfo();

            // 処理タイマー開始
            sw.Restart();
        }
    }

    #region リニアパス作成
    /// <summary>
    /// リニアループのポイント作成
    /// </summary>
    private void CreateLinearPathPoints()
    {
        // カーブセグメント取得
        var curveNameList = new List<string>();
        var curveSegments = new List<GameObject>();
        if (linearSetting.type == "XTS")
        {
            curveNameList.Add("081434");
            curveNameList.Add("AT2050");
        }
        curveSegments = moveObject.GetComponentsInChildren<Transform>(true)
            .Where(t => curveNameList.Any(d => t.name.Contains(d)))
            .Select(t => t.gameObject).ToList();
        // 真下の子供だけ取得
        foreach (var child in unitSetting.childrenObject.FindAll(d => d.GetComponent<AxisMotionBase>() == null))
        {
            if (curveNameList.Contains(child.name))
            {
                curveSegments.Add(child);
            }
        }
        // メッシュフィルター取得
        var meshFilters = new List<MeshFilter>();
        foreach (var curve in curveSegments)
        {
            foreach (var mesh in curve.GetComponentsInChildren<MeshFilter>())
            {
                if (!meshFilters.Contains(mesh))
                {
                    meshFilters.Add(mesh);
                }
            }
        }
        // カーブメッシュだけ取得
        if (meshFilters.Count > 0)
        {
            // 全頂点情報取得
            var allVerts = new List<VerticeInfo>();
            var index = 0;
            foreach (var mf in meshFilters)
            {
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                for (int i = 0; i < verts.Length; i++)
                {
                    allVerts.Add(new VerticeInfo
                    {
                        id = index,
                        meshId = meshFilters.IndexOf(mf),
                        vertice = moveObject.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[i])),
                        normal = moveObject.transform.InverseTransformPoint(mf.transform.TransformPoint(normals[i]))
                    });
                    index++;
                }
            }
            // 全点の最大最小を取得
            var m = new List<float>{
                        allVerts.Max(d => d.vertice.x) - allVerts.Min(d => d.vertice.x),
                        allVerts.Max(d => d.vertice.y) - allVerts.Min(d => d.vertice.y),
                        allVerts.Max(d => d.vertice.z) - allVerts.Min(d => d.vertice.z)
                    };
            // 奥行方向算出
            dirD = m.Min() == m[0] ? 0 : m.Min() == m[1] ? 1 : 2;
            // 流れ方向算出
            dirL = m.Max() == m[0] ? 0 : m.Max() == m[1] ? 1 : 2;
            // 高さ方向算出
            dirH = 3 - dirL - dirD;
            // 奥行方向を削除
            allVerts = allVerts.Select(d => new VerticeInfo
            {
                id = d.id,
                meshId = d.meshId,
                vertice = new Vector3(dirD == 0 ? 0f : d.vertice.x, dirD == 1 ? 0f : d.vertice.y, dirD == 2 ? 0f : d.vertice.z),
                normal = d.normal
            }).ToList();
            // 許容誤差（必要に応じて調整）
            float tolerance = 0.0000001f;
            // 同一点を削除(0.1μm以下は同一の点とする)
            allVerts = allVerts.GroupBy(v => new
            {
                x = Mathf.Round(v.vertice.x / tolerance),
                y = Mathf.Round(v.vertice.y / tolerance),
                z = Mathf.Round(v.vertice.z / tolerance)
            }).Select(g => g.First()).ToList();
            // 疑似中心取得
            center = new Vector3(
                dirD == 0 ? 0 : allVerts.Average(d => d.vertice.x),
                dirD == 1 ? 0 : allVerts.Average(d => d.vertice.y),
                dirD == 2 ? 0 : allVerts.Average(d => d.vertice.z)
            );
            // 流れ方向で半分に分ける
            var plusVerts = allVerts.FindAll(d =>
                dirL == 0 ? d.vertice.x > center.x :
                dirL == 1 ? d.vertice.y > center.y :
                d.vertice.z > center.z
            );
            var minusVerts = allVerts.FindAll(d =>
                dirL == 0 ? d.vertice.x < center.x :
                dirL == 1 ? d.vertice.y < center.y :
                d.vertice.z < center.z
            );
            // 四隅取得
            var point1 = minusVerts.OrderByDescending(d => dirH == 0 ? d.vertice.x : dirH == 1 ? d.vertice.y : d.vertice.z).First();
            var point2 = plusVerts.OrderByDescending(d => dirH == 0 ? d.vertice.x : dirH == 1 ? d.vertice.y : d.vertice.z).First();
            var point3 = plusVerts.OrderBy(d => dirH == 0 ? d.vertice.x : dirH == 1 ? d.vertice.y : d.vertice.z).First();
            var point4 = minusVerts.OrderBy(d => dirH == 0 ? d.vertice.x : dirH == 1 ? d.vertice.y : d.vertice.z).First();
            var lMin = dirL == 0 ? point4.vertice.x : dirL == 1 ? point4.vertice.y : point4.vertice.z;
            var lMax = dirL == 0 ? point3.vertice.x : dirL == 1 ? point3.vertice.y : point3.vertice.z;
            var hMin = dirH == 0 ? point3.vertice.x : dirH == 1 ? point3.vertice.y : point3.vertice.z;
            var hMax = dirH == 0 ? point2.vertice.x : dirH == 1 ? point2.vertice.y : point2.vertice.z;
            // 外周を作成する
            var outlines = new List<VerticeInfo>();
            outlines.AddRange(plusVerts);
            outlines.AddRange(minusVerts);
            // 中心算出
            center = new Vector3(
                dirL == 0 ? (lMax - lMin) / 2 + lMin : dirH == 0 ? (hMax - hMin) / 2 + hMin : point3.vertice.x,
                dirL == 1 ? (lMax - lMin) / 2 + lMin : dirH == 1 ? (hMax - hMin) / 2 + hMin : point3.vertice.y,
                dirL == 2 ? (lMax - lMin) / 2 + lMin : dirH == 2 ? (hMax - hMin) / 2 + hMin : point3.vertice.z
            );
            // 凸凹を無くす
            outlines = ConvexHull(outlines);
            // 四隅が消えてる可能性があるのでチェックしてなくなっていれば追加
            if (outlines.Find(d => d.id == point1.id) == null)
            {
                outlines.Add(point1);
            }
            if (outlines.Find(d => d.id == point2.id) == null)
            {
                outlines.Add(point2);
            }
            if (outlines.Find(d => d.id == point3.id) == null)
            {
                outlines.Add(point3);
            }
            if (outlines.Find(d => d.id == point4.id) == null)
            {
                outlines.Add(point4);
            }
            // 回転方向にソート
            outlines = outlines.OrderByDescending(d =>
            {
                double angle = Math.Atan2(
                    dirH == 0 ? d.vertice.x - center.x : dirH == 1 ? d.vertice.y - center.y : d.vertice.z - center.z,
                    dirL == 0 ? d.vertice.x - center.x : dirL == 1 ? d.vertice.y - center.y : d.vertice.z - center.z
                );
                if (angle < 0) angle += Math.PI * 2; // 0〜2πに正規化
                return angle;
            }).ToList();
            // 奥行方向をムーバーオブジェクトと合わせる
            linearSetting.gameObject.transform.parent = moveObject.transform;
            // ムーバーの位置と一番近い点取得
            near = outlines.OrderBy(d => Vector3.Distance(linearSetting.gameObject.transform.localPosition, d.vertice)).First();
            if (dirH == 0)
            {
                moverOffsetH = linearSetting.gameObject.transform.localPosition.x - near.vertice.x;
                if ((hMin < linearSetting.gameObject.transform.localPosition.x) && (hMax > linearSetting.gameObject.transform.localPosition.x))
                {
                    moverOffsetH = -moverOffsetH;
                }
            }
            else if (dirH == 1)
            {
                moverOffsetH = linearSetting.gameObject.transform.localPosition.y - near.vertice.y;
                if ((hMin < linearSetting.gameObject.transform.localPosition.y) && (hMax > linearSetting.gameObject.transform.localPosition.y))
                {
                    moverOffsetH = -moverOffsetH;
                }
            }
            else
            {
                moverOffsetH = linearSetting.gameObject.transform.localPosition.z - near.vertice.z;
                if ((hMin < linearSetting.gameObject.transform.localPosition.z) && (hMax > linearSetting.gameObject.transform.localPosition.z))
                {
                    moverOffsetH = -moverOffsetH;
                }
            }
            // 一番近い距離から並べる
            index = outlines.FindIndex(d => d.id == near.id);
            outlines = outlines.Skip(index).Concat(outlines.Take(index)).ToList();
            // リニア距離分外側へオフセット
            outlines = OffsetPath(outlines, moverOffsetH);
            // 逆転判定
            if (linearSetting.rvs)
            {
                outlines = outlines.AsEnumerable().Reverse().ToList();
            }
            // ムーバー初期オフセット取得
            loopPathPoints = outlines.Select(d =>
            {
                var pos = new Vector3
                {
                    x = dirD == 0 ? linearSetting.gameObject.transform.localPosition.x : d.vertice.x,
                    y = dirD == 1 ? linearSetting.gameObject.transform.localPosition.y : d.vertice.y,
                    z = dirD == 2 ? linearSetting.gameObject.transform.localPosition.z : d.vertice.z
                };
                return pos;
            }).ToList();
            // 一旦パスセット
            loopPathPoints.Add(loopPathPoints[0]);
            GetPositionOnPath(0, out Vector3 pos, out Vector3 dir, out Quaternion rot);
            if (dir != Vector3.zero)
            {
                // ダミームーバーで初期角度取得
                var mover = new GameObject();
                mover.transform.parent = moveObject.transform;
                mover.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                // 初期オフセット
                moverOffsetAng = mover.transform.localEulerAngles - linearSetting.gameObject.transform.localEulerAngles;
                Destroy(mover);
            }
            // 順番入れ替え(正しくないときはそれなりに)
            var startId = point1.id;
            startId = linearSetting.org == 0 ? point1.id :
                      linearSetting.org == 1 ? point2.id :
                      linearSetting.org == 2 ? point3.id : point4.id;
            index = outlines.FindIndex(d => d.id == startId);
            outlines = outlines.Skip(index).Concat(outlines.Take(index)).ToList();
            // 同一点を削除(1mm以下は同一の点とする)
            tolerance = 0.001f;
            outlines = outlines.GroupBy(v => new
            {
                x = Mathf.Round(v.vertice.x / tolerance),
                y = Mathf.Round(v.vertice.y / tolerance),
                z = Mathf.Round(v.vertice.z / tolerance)
            }).Select(g => g.First()).ToList();
            // 開始点セット
            outlines.Add(outlines[0]);
            // パスの総距離を計算l
            calcLength = 0f;
            for (int i = 0; i < outlines.Count - 1; i++)
            {
                calcLength += Vector3.Distance(outlines[i].vertice, outlines[i + 1].vertice);
            }
            // カーブ距離算出
            calcCurveLength = calcLength / 2 - straightLength;
            // パスにセット
            loopPathPoints = outlines.Select(d =>
            {
                var pos = new Vector3
                {
                    x = dirD == 0 ? linearSetting.gameObject.transform.localPosition.x : d.vertice.x,
                    y = dirD == 1 ? linearSetting.gameObject.transform.localPosition.y : d.vertice.y,
                    z = dirD == 2 ? linearSetting.gameObject.transform.localPosition.z : d.vertice.z
                };
                return pos;
            }).ToList();
        }
    }

    /// <summary>
    /// 平面上で凸包を計算
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    private List<VerticeInfo> ConvexHull(List<VerticeInfo> points)
    {
        // X座標でソート
        var sorted = points
            .OrderBy(p => dirL == 0 ? p.vertice.x : dirL == 1 ? p.vertice.y : p.vertice.z)
            .ThenBy(p => dirH == 0 ? p.vertice.x : dirH == 1 ? p.vertice.y : p.vertice.z)
            .ToList();

        var hull = new List<VerticeInfo>();

        // 下側
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2].vertice, hull[hull.Count - 1].vertice, p.vertice) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        // 上側
        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count >= lower && Cross(hull[hull.Count - 2].vertice, hull[hull.Count - 1].vertice, p.vertice) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    float Cross(Vector3 o, Vector3 a, Vector3 b)
    {
        var ax = dirL == 0 ? (a.x - o.x) : dirL == 1 ? (a.y - o.y) : (a.z - o.z);
        var bz = dirH == 0 ? (b.x - o.x) : dirH == 1 ? (b.y - o.y) : (b.z - o.z);
        var az = dirH == 0 ? (a.x - o.x) : dirH == 1 ? (a.y - o.y) : (a.z - o.z);
        var bx = dirL == 0 ? (b.x - o.x) : dirL == 1 ? (b.y - o.y) : (b.z - o.z);
        return ax * bz - az * bx;
    }

    /// <summary>
    /// パスを外側にする
    /// </summary>
    /// <param name="path"></param>
    /// <param name="offsetDistance"></param>
    /// <returns></returns>
    private List<VerticeInfo> OffsetPath(List<VerticeInfo> path, float offsetDistance)
    {
        var result = new List<VerticeInfo>();
        for (int i = 0; i < path.Count; i++)
        {
            // 前後の点を取得（ループ対応）
            int prev = (i - 1 + path.Count) % path.Count;
            int next = (i + 1) % path.Count;

            // 前後の接線ベクトルを計算
            Vector3 dirPrev = (path[i].vertice - path[prev].vertice).normalized;
            Vector3 dirNext = (path[next].vertice - path[i].vertice).normalized;

            // 平均接線ベクトル
            Vector3 tangent = (dirPrev + dirNext).normalized;

            // XZ平面上の法線（接線を90度回転）
            Vector3 normal = Vector3.zero;
            if (dirL == 0)
            {
                if (dirH == 1)
                {
                    normal = new Vector3(-tangent.y, tangent.x, 0f).normalized;
                }
                if (dirH == 2)
                {
                    normal = new Vector3(-tangent.z, 0f, tangent.x).normalized;
                }
            }

            // 外側にオフセット
            result.Add(new VerticeInfo
            {
                id = path[i].id,
                normal = path[i].normal,
                vertice = path[i].vertice + normal * offsetDistance
            });
        }
        return result;
    }
    #endregion リニアパス作成

    #region ムーバー作成
    /// <summary>
    /// ムーバーオブジェクト作成
    /// </summary>
    private void CreateMoverObject()
    {
        // ヘッドオブジェクト取得
        var heads = unitSetting.childrenObject.FindAll(d => d.GetComponent<AxisMotionBase>() != null);
        // ムーバーオブジェクトにヘッドをセット
        foreach (var head in heads)
        {
            head.transform.parent = linearSetting.gameObject.transform;
        }
        // ムーバーオブジェクトを無効化
        linearSetting.gameObject.SetActive(false);
        if (loopPathPoints.Count > 4)
        {
            // バケット間隔
            moverPitch = linearSetting.pitch / 1000f;
            moverOffsetPos = linearSetting.offset / 1000f;
            totalLength = linearSetting.length / 1000f;
            curveLength = totalLength / 2 - straightLength;
            // 初期位置から
            for (var i = 0; i < linearSetting.count; i++)
            {
                var mover = new MoverInfo
                {
                    id = i,
                    obj = Instantiate(linearSetting.gameObject),
                    pointno = -1,
                    pos = (totalLength - moverPitch * i) % totalLength,
                    size = moverPitch,
                    length = totalLength
                };
                if (linearSetting.stat && linearSetting.statPos.Count >= 3)
                {
                    mover.CreateStatus(new Vector3(linearSetting.statPos[0] / 1000f, linearSetting.statPos[1] / 1000f, linearSetting.statPos[2] / 1000f));
                }
                // パス上のその距離の位置を取得
                GetPositionOnPath(mover.pos, out Vector3 pos, out Vector3 dir, out Quaternion rot);
                mover.obj.transform.parent = moveObject.transform.parent;
                mover.obj.transform.localPosition = pos;
                if (dir != Vector3.zero)
                {
                    if (dirL == 0)
                    {
                        if (dirH == 1)
                        {
                            mover.obj.transform.localRotation = rot * Quaternion.Euler(moverOffsetAng);
                        }
                        else
                        {

                            mover.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                            mover.obj.transform.localEulerAngles -= moverOffsetAng;
                        }
                    }
                }
                mover.obj.SetActive(true);
                movers.Add(mover);
            }
            // ポインタセット
            for (var i = 0; i < movers.Count; i++)
            {
                if (i == 0)
                {
                    movers[i].prv = movers[movers.Count - 1];
                }
                else
                {
                    movers[i].prv = movers[i - 1];
                }
                if (i == movers.Count - 1)
                {
                    movers[i].next = movers[0];
                }
                else
                {
                    movers[i].next = movers[i + 1];
                }
            }
        }
    }
    #endregion ムーバー作成

    #region ポイント作成
    /// <summary>
    /// ポイント作成
    /// </summary>
    private void CreatePointInfo()
    {
        var points = linearSetting.points.OrderBy(d => d.pos).ToList();
        var init = points.Find(d => d.name == "init");
        if (init == null)
        {
            init = new LinearSetting.PointInfo
            {
                name = "init",
                pos = 0,
                count = 0,
                type = "BUFF",
            };
            points.Add(init);
            points = points.OrderBy(d => d.pos).ToList();
        }
        else
        {
            init.count = 0;
            init.type = "BUFF";
        }
        for (var i = 0; i < points.Count; i++)
        {
            var spd = linearSetting.spds.Count > points[i].spd ? linearSetting.spds[points[i].spd] : new LinearSetting.SpdInfo();
            var point = new PointInfo
            {
                name = points[i].name,
                id = this.points.Count,
                pos = points[i].pos / 1000f,
                totalLength = totalLength,
                actTag = points[i].tagAct,
                processTag = points[i].tagProcess,
                finTag = points[i].tagFin,
                type = points[i].type == "PP" ? PointType.PP : 
                       points[i].type == "BUFF" ? PointType.BUFF : 
                       points[i].type == "TP" ? PointType.TP : PointType.MTP,
                processTime = points[i].wait,
                spdInfo = new SpeedInfo
                {
                    vm = spd.vm / 1000f,
                    vf = spd.vf / 1000f,
                    ve = spd.ve / 1000f,
                    acl = spd.acl * 9.8f,
                    dcl = spd.dcl * 9.8f,
                    jerkA = spd.jerkA,
                    jerkD = spd.jerkD,
                }
            };
            // タグ作成
            if (point.actTag != "")
            {
                if (!tags.ContainsKey(point.actTag))
                {
                    tags.Add(point.actTag, new TagStatus());
                }
            }
            if (point.processTag != "")
            {
                if (!tags.ContainsKey(point.processTag))
                {
                    tags.Add(point.processTag, new TagStatus());
                }
            }
            if (point.finTag != "")
            {
                if (!tags.ContainsKey(point.finTag))
                {
                    tags.Add(point.finTag, new TagStatus());
                }
            }
            if (init == points[i])
            {
                // 初期位置
                point.isInit = true;
                point.type = PointType.BUFF;
            }
            // ポイント数
            var count = points[i].count;
            if ((point.type == PointType.BUFF) && (count == 0))
            {
                // 全ムーバー停止出来るように
                count = linearSetting.count;
            }
            else if (point.type == PointType.MTP)
            {
                // 複数通過なら1
                count = 1;
            }
            // このポイントの整列ピッチ：PPのみ points[i].pitch(mm) が全体ピッチより大きければ採用（BUFF等は従来どおり moverPitch）
            var alignPitch = (point.type == PointType.PP)
                ? Mathf.Max(moverPitch, points[i].pitch / 1000f)
                : moverPitch;
            for (var j = 0; j < count; j++)
            {
                var pos = point.type == PointType.TP ? point.pos : (totalLength + (point.pos - alignPitch * j)) % totalLength;
                point.stopPoints.Add(new StopPoint
                {
                    pointId = this.points.Count,
                    stopId = j,
                    isPP = point.type == PointType.PP,
                    isTP = point.type == PointType.TP,
                    isMTP = point.type == PointType.MTP,
                    target = pos
                });
            }
            this.points.Add(point);
            // ポイントタイプ別処理
            if ((point.type == PointType.TP) || (point.type == PointType.MTP))
            {
                // 通過ポイントならポイント到着後の終了ポイント作成
                point = new PointInfo
                {
                    id = this.points.Count,
                    name = points[i].name + "(通過区間)",
                    pos = points[i].pos / 1000f + points[i].wait / 1000f,
                    totalLength = totalLength,
                    actTag = points[i].tagAct,
                    finTag = points[i].tagFin,
                    type = point.type == PointType.TP ? PointType.TPE : PointType.MTPE,
                    spdInfo = new SpeedInfo
                    {
                        vm = spd.ve / 1000f,
                        vf = spd.ve / 1000f,
                        ve = spd.ve / 1000f,
                        acl = spd.acl * 9.8f,
                        dcl = spd.dcl * 9.8f,
                        jerkA = spd.jerkA,
                        jerkD = spd.jerkD,
                    }
                };
                // 複数通過ポイントなら1固定
                for (var j = 0; j < count; j++)
                {
                    var pos = point.pos;
                    point.stopPoints.Add(new StopPoint
                    {
                        pointId = this.points.Count,
                        stopId = j,
                        isTPE = point.type == PointType.TPE,
                        isMTPE = point.type == PointType.MTPE,
                        target = pos
                    });
                }
                this.points.Add(point);
            }
        }

        // ポインタセット
        StopPoint sp = null;
        StopPoint spf = null;
        for (var i = 0; i < this.points.Count; i++)
        {
            foreach (var p in Enumerable.Reverse(this.points[i].stopPoints))
            {
                if (sp != null)
                {
                    sp.next = p;
                }
                else
                {
                    spf = p;
                }
                sp = p;
            }
            if (i == 0)
            {
                this.points[i].prv = this.points[this.points.Count - 1];
            }
            else
            {
                this.points[i].prv = this.points[i - 1];
            }
            if (i == this.points.Count - 1)
            {
                sp.next = spf;
                this.points[i].next = this.points[0];
            }
            else
            {
                this.points[i].next = this.points[i + 1];
            }
        }
        foreach(var point in this.points)
        {
            var p = point.next;
            for (var i = 0; i < this.points.Count; i++, p = p.next)
            {
                if (point.isMTPE)
                {
                    if ((p.type == PointType.PP) ||
                        (p.type == PointType.TP))
                    {
                        point.nextPP = p;
                        break;
                    }
                }
                else
                {
                    if ((p.type == PointType.PP) ||
                        (p.type == PointType.TP) ||
                        (p.type == PointType.TPE) ||
                        (p.type == PointType.MTP) ||
                        (p.type == PointType.MTPE))
                    {
                        point.nextPP = p;
                        break;
                    }
                }
            }
        }
        // 初期位置設定
        var initPoint = this.points.Find(d => d.isInit);
        if (movers.Count > 0)
        {
            var mover = movers[0];
            do
            {
                initPoint.SetMover(mover);
                mover = mover.next;
            } while (!mover.isHead);
        }
    }
    #endregion ポイント作成

    #region 位置算出
    /// <summary>
    /// パス上のポイント取得
    /// </summary>
    /// <param name="path"></param>
    /// <param name="distance"></param>
    /// <param name="pos"></param>
    /// <param name="dir"></param>
    private void GetPositionOnPath(float distance, out Vector3 pos, out Vector3 dir, out Quaternion rot)
    {
        Vector3 toPos = Vector3.zero;
        float accumulated = 0f;
        distance = CalcLinearPos(distance + moverOffsetPos);
        for (int i = 0; i < loopPathPoints.Count - 1; i++)
        {
            float segLen = Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);

            if (accumulated + segLen >= distance)
            {
                float t = (distance - accumulated) / segLen;
                pos = Vector3.Lerp(loopPathPoints[i], loopPathPoints[i + 1], t);
                dir = (loopPathPoints[i + 1] - loopPathPoints[i]).normalized;
                if (!linearSetting.rvs)
                {
                    dir = -dir;
                }
                // 角度算出
                if (dirL == 0)
                {
                    if (dirD == 1)
                    {
                        toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
                    }
                    else if (dirD == 2)
                    {
                        toPos = (pos - new Vector3(center.x, center.y, pos.z)).normalized;
                    }
                }
                rot = Quaternion.LookRotation(dir, toPos);
                return;
            }
            accumulated += segLen;
        }
        // パスの終端
        pos = loopPathPoints[loopPathPoints.Count - 1];
        dir = (loopPathPoints[loopPathPoints.Count - 1] - loopPathPoints[loopPathPoints.Count - 2]).normalized;
        if (!linearSetting.rvs)
        {
            dir = -dir;
        }
        // 角度算出
        if (dirL == 0)
        {
            if (dirD == 1)
            {
                toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
            }
            else if (dirD == 2)
            {
                toPos = (pos - new Vector3(center.x, center.y, pos.z)).normalized;
            }
        }
        rot = Quaternion.LookRotation(dir, toPos);
    }

    /// <summary>
    /// リニア位置を計算
    /// </summary>
    /// <returns></returns>
    private float CalcLinearPos(float pos)
    {
        var ret = (pos + totalLength) % totalLength;
        if (pos <= straightLength)
        {
        }
        else if (pos <= straightLength + curveLength)
        {
            ret = straightLength + (pos - straightLength) * calcCurveLength / curveLength;
        }
        else if (pos <= straightLength * 2 + curveLength)
        {
            ret = straightLength + calcCurveLength + (pos - straightLength - curveLength);
        }
        else
        {
            ret = straightLength * 2 + calcCurveLength + +(pos - straightLength - straightLength - curveLength) * calcCurveLength / curveLength;
        }
        return ret;
    }
    #endregion 位置算出

    #region 速度カーブ作成
    /// <summary>
    /// 速度カーブ作成
    /// </summary>
    /// <param name="distance"></param>
    /// <param name="startVel"></param>
    /// <param name="endVel"></param>
    /// <param name="maxVel"></param>
    /// <param name="maxAccel"></param>
    /// <param name="maxDecel"></param>
    /// <returns></returns>
    public static List<MotionPoint> GenerateProfile(
        float distance,
        float startVel,
        float endVel,
        float maxVel,
        float maxAccel,
        float maxDecel,
        ref int tmAcl,
        ref int tmEven
    )
    {
        var table = new List<MotionPoint>();

        // 加速フェーズ時間・距離
        float t_accel = (maxVel - startVel) / maxAccel;
        float d_accel = (startVel + maxVel) * t_accel * 0.5f;

        // 減速フェーズ時間・距離
        float t_decel = (maxVel - endVel) / maxDecel;
        float d_decel = (maxVel + endVel) * t_decel * 0.5f;

        // 最大速度に達しない場合の補正
        if (d_accel + d_decel > distance)
        {
            // 加速・減速が異なる場合の到達可能最大速度
            // v^2 = startVel^2 + 2*a*d_a = endVel^2 + 2*d*d_d
            // d_a + d_d = distance を連立して解く
            maxVel = Mathf.Sqrt(
                (2f * maxAccel * maxDecel * distance + maxDecel * startVel * startVel + maxAccel * endVel * endVel)
                / (maxAccel + maxDecel)
            );
            t_accel = (maxVel - startVel) / maxAccel;
            d_accel = (startVel + maxVel) * t_accel * 0.5f;
            t_decel = (maxVel - endVel) / maxDecel;
            d_decel = (maxVel + endVel) * t_decel * 0.5f;
        }

        // 定速フェーズ
        float d_const = distance - d_accel - d_decel;
        float t_const = d_const / maxVel;

        // フェーズ境界時間
        float T1 = t_accel;
        float T2 = T1 + t_const;
        float T3 = T2 + t_decel;

        int totalMs = Mathf.CeilToInt(T3 * 1000f);

        for (int ms = 0; ms <= totalMs; ms++)
        {
            float t = ms * 0.001f;
            float pos, vel, acc;

            if (t <= T1) // 加速フェーズ
            {
                float dt = t;
                acc = maxAccel;
                vel = startVel + maxAccel * dt;
                pos = startVel * dt + 0.5f * maxAccel * dt * dt;
            }
            else if (t <= T2) // 定速フェーズ
            {
                float dt = t - T1;
                acc = 0f;
                vel = maxVel;
                pos = d_accel + maxVel * dt;
            }
            else // 減速フェーズ
            {
                float dt = t - T2;
                acc = -maxDecel;
                vel = maxVel - maxDecel * dt;
                pos = d_accel + d_const + maxVel * dt - 0.5f * maxDecel * dt * dt;
            }
            table.Add(new MotionPoint
            {
                time = ms,
                position = pos,
                velocity = vel,
                accel = acc,
                jerk = 0f
            });
        }
        // ループの最後に最終点を補正
        if (table.Count > 0)
        {
            var last = table[table.Count - 1];
            table[table.Count - 1] = new MotionPoint
            {
                time = last.time,
                position = distance,  // 正確に目標距離に設定
                velocity = endVel,    // 正確に終了速度に設定
                accel = 0f,
                jerk = 0f
            };
        }
        tmAcl = (int)(T1 * 1000);
        tmEven = (int)(T2 * 1000);
        return table;
    }
    #endregion 速度カーブ作成
    #endregion 関数定義
}