using NUnit.Framework;
using Org.BouncyCastle.Asn1.BC;
using Org.BouncyCastle.Ocsp;
using Parameters;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using static MotionLinear;
using static Parameters.LinearSetting;
using static UnityEngine.Rendering.DebugUI;

public class MotionLinear : AxisMotionBase
{
    #region 列挙型定義
    /// <summary>
    /// ポイントタイプ
    /// </summary>
    protected enum PointType
    {
        PP,
        BUFF,
        TP,
        TPE,
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
    protected class MotionTable
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
        /// 動作テーブル
        /// </summary>
        public List<MotionPoint> table = new List<MotionPoint>();
    }

    /// <summary>
    /// ムーバー情報
    /// </summary>
    protected class MoverInfo
    {
        /// <summary>
        /// ID
        /// </summary>
        public int id;
        /// <summary>
        /// オブジェクト
        /// </summary>
        public GameObject obj;
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
        /// モーションセット
        /// </summary>
        public void SetMotion(float target, float dist, SpeedInfo spdInfo)
        {
            motion.pos = 0;
            motion.target = target;
            motion.vf = motion.spd;
            motion.ve = spdInfo.ve;
            motion.start = pos;
            motion.table = GenerateProfile(dist, motion.vf, spdInfo.ve, spdInfo.vm, spdInfo.acl, spdInfo.dcl);
            motion.act = true;
            motion.fin = false;
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
                    pos = motion.pos;
                    ret = pos;
                }
                else
                {
                    ret = motion.pos + motion.start;
                }
                if (Mathf.Abs(ret - prv.pos) <= size - 0.000001)
                {
                    // 1μm以上で接触
                    ret = ((prv.pos - size) + length) % length;
                    motion.pos = ret - motion.start;
                    var index = motion.table.FindIndex(d => d.position >= motion.pos);
                    if (index >= 0)
                    {
                        var t = motion.table[index];
                        motion.lapsOffset += (motion.laps - t.time);
                        // 動作中に戻す
                        motion.act = true;
                        motion.fin = false;
                    }
                }
            }
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
    }

    /// <summary>
    /// 停止ポイント
    /// </summary>
    protected class StopPoint
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
        /// 通過終了ポイント
        /// </summary>
        public bool isTPE;
        /// <summary>
        /// 割当ムーバー
        /// </summary>
        public MoverInfo? mover = null;
        /// <summary>
        /// 通過ポイント
        /// </summary>
        public StopPoint? tp = null;
        /// <summary>
        /// 通過終了ポイントへ移動中
        /// </summary>
        public bool isMoveToTPE = false;
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
    protected class PointInfo
    {
        /// <summary>
        /// ID
        /// </summary>
        public int id;
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
        /// 速度情報
        /// </summary>
        public SpeedInfo tpSpdInfo;
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
        /// 処理可能か
        /// </summary>
        public bool isEnableMove
        {
            get
            {
                var ret = false;
                foreach (var sp in stopPoints)
                {
                    if (sp.mover == null)
                    {
                        continue;
                    }
                    ret |= sp.mover.motion.fin;
                }
                return ret;
            }
        }
        /// <summary>
        /// 処理完了
        /// </summary>
        public bool isProcessed
        {
            get
            {
                if (type == PointType.PP)
                {
                    // プロセスポイントの場合
                    if (isEnableProcess)
                    {
                        // 処理可能状態
                        if (!isProcess)
                        {
                            //　動作完了初回
                            isProcess = true;
                            lapsOffset = cycle;
                            return false;
                        }
                        else
                        {
                            isProcess = (processTime > cycle - lapsOffset);
                            if (!isProcess)
                            {
                                // 処理完了
                                foreach (var sp in stopPoints)
                                {
                                    sp.mover.motion.processed = true;
                                }
                            }
                            return !isProcess;
                        }
                    }
                    else
                    {
                        return isEnableMove;
                    }
                }
                else if (type == PointType.TP)
                {
                    foreach (var sp in stopPoints)
                    {
                        if ((sp.mover != null) && !sp.isMoveToTPE)
                        {
                            if (sp.mover.motion.fin)
                            {
                                if (nextPP.nextMover != null)
                                {
                                    // 通過中保持
                                    nextPP.nextMover.tp = sp;
                                    // 通過終了ポイントへ
                                    SetMover(sp, nextPP.nextMover, tpSpdInfo);
                                    sp.isMoveToTPE = true;
                                }
                            }
                        }
                    }
                    return false;
                }
                else if (type == PointType.TPE)
                {
                    foreach (var sp in stopPoints)
                    {
                        if (sp.mover != null)
                        {
                            if (sp.mover.motion.fin)
                            {
                                // 次のポイントへ
                                StopPoint target = null;
                                for (var p = sp.next; p.pointId != nextPP.next.id; p = p.next)
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
                                    // 次のポイントへ
                                    SetMover(sp, target);
                                    sp.mover = null;
                                    sp.tp.mover = null;
                                    sp.tp.isMoveToTPE = false;
                                    sp.tp = null;
                                }
                            }
                        }
                    }
                    return false;
                }
                else
                {
                    return true;
                }
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
        public void SetMover(StopPoint nowPoint, StopPoint nextPoint, SpeedInfo spd = null)
        {
            var mover = nowPoint.mover;
            var next = nextPoint == null ? nextMover : nextPoint;
            if (next != null)
            {
                var dist = (next.target + totalLength - mover.pos) % totalLength;
                next.mover = mover;
                mover.SetMotion(next.target, dist, spd == null ? spdInfo : spd);
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
    protected List<MoverInfo> movers = new List<MoverInfo>();

    /// <summary>
    /// ポイント情報
    /// </summary>
    protected List<PointInfo> points = new List<PointInfo>();

    /// <summary>
    /// タグ情報
    /// </summary>
    protected Dictionary<string, TagStatus> tags = new Dictionary<string, TagStatus>();

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
    /// リニアの動作方向
    /// </summary>
    private bool isLinearRvs;

    /// <summary>
    /// モデル中心
    /// </summary>
    private Vector3 center;

    /// <summary>
    /// タイマー
    /// </summary>
    public System.Diagnostics.Stopwatch sw = new();
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
        // ポイント更新
        foreach (var point in points)
        {
            point.cycle = cycle;
            if (!point.isBlank && point.isProcessed)
            {
                var isPP = tags[point.nextPP.actTag].value;
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
                                target = p;
                            }
                            else
                            {
                                if (!p.isTP && !p.isTPE)
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
                        }
                    }
                }
            }
        }

        // ムーバー位置更新
        foreach (var mover in movers)
        {
            mover.cycle = cycle;
            mover.pos = mover.RenewPosition() % totalLength;
            RenewMoverPosition(mover);
        }
    }

    /// <summary>
    /// ムーバーをの位置を更新
    /// </summary>
    /// <param name="mover"></param>
    private void RenewMoverPosition(MoverInfo mover)
    {
//        Debug.Log(mover.pos.ToString());
        GetPositionOnPath(mover.pos, out Vector3 pos, out Vector3 dir);
        mover.obj.transform.parent = moveObject.transform.parent;
        mover.obj.transform.localPosition = pos;
        if (dir != Vector3.zero)
        {
            // 円弧の中心からposへの方向を「上」として使う
            Vector3 toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
            Quaternion rot = Quaternion.LookRotation(dir, toPos);
            mover.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
            mover.obj.transform.localEulerAngles -= moverOffsetAng;
        }
    }

    /// <summary>
    /// モデル再構築
    /// </summary>
    protected override void PreModelRestruct()
    {
        base.PreModelRestruct();

        if (linearSetting != null)
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
        var meshFilters = new List<MeshFilter>();
        meshFilters.AddRange(moveObject.GetComponentsInChildren<MeshFilter>());
        // 真下の子供だけ取得
        foreach (var child in unitSetting.childrenObject.FindAll(d => d.GetComponent<AxisMotionBase>() == null))
        {
            meshFilters.AddRange(child.GetComponentsInChildren<MeshFilter>());
        }
        if (meshFilters.Count > 0)
        {
            // 全頂点情報取得
            var allVerts = new List<VerticeInfo>();
            foreach (var mf in meshFilters)
            {
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                for (int i = 0; i < verts.Length; i++)
                {
                    allVerts.Add(new VerticeInfo
                    {
                        vertice = moveObject.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[i])),
                        normal = moveObject.transform.InverseTransformPoint(mf.transform.TransformPoint(normals[i]))
                    });
                }
            }
            // 許容誤差（必要に応じて調整）
            float tolerance = 0.0000001f;
            // 同一点を削除(1μm以下は同一の点とする)
            allVerts = allVerts.GroupBy(v => new
            {
                x = Mathf.Round(v.vertice.x / tolerance),
                y = Mathf.Round(v.vertice.y / tolerance),
                z = Mathf.Round(v.vertice.z / tolerance)
            }).Select(g => g.First()).ToList();
            var m = new List<float>{
                        allVerts.Max(d => d.vertice.x) - allVerts.Min(d => d.vertice.x),
                        allVerts.Max(d => d.vertice.y) - allVerts.Min(d => d.vertice.y),
                        allVerts.Max(d => d.vertice.z) - allVerts.Min(d => d.vertice.z)
                    };
            // 奥行方向算出
            var min = m.Min() == m[0] ? 0 : m.Min() == m[1] ? 1 : 2;
            // 流れ方向算出
            var max = m.Max() == m[0] ? 0 : m.Max() == m[1] ? 1 : 2;
            // 高さ方向算出
            var mid = 3 - max - min;
            // 疑似中心取得
            center = new Vector3(
                min == 0 ? 0 : allVerts.Average(d => d.vertice.x),
                min == 1 ? 0 : allVerts.Average(d => d.vertice.y),
                min == 2 ? 0 : allVerts.Average(d => d.vertice.z)
            );
            // 位置遠い点を取得
            var farthest = allVerts.OrderByDescending(v => Vector3.Distance(
                new Vector3(
                    min == 0 ? 0 : v.vertice.x,
                    min == 1 ? 0 : v.vertice.y,
                    min == 2 ? 0 : v.vertice.z),
                center)).First();
            // 一番遠い点と同じ外周を取得
            var outlines = allVerts.FindAll(d =>
                min == 0 ? Math.Abs(d.vertice.x - farthest.vertice.x) < tolerance :
                min == 1 ? Math.Abs(d.vertice.y - farthest.vertice.y) < tolerance :
                Math.Abs(d.vertice.z - farthest.vertice.z) < tolerance);
            // 高さ方向の最大最小取得
            var hMin = outlines.Min(d => mid == 0 ? d.vertice.x : mid == 1 ? d.vertice.y : d.vertice.z);
            var hMax = outlines.Max(d => mid == 0 ? d.vertice.x : mid == 1 ? d.vertice.y : d.vertice.z);
            var hMinVer = outlines.FindAll(d =>
                mid == 0 ? Math.Abs(d.vertice.x - hMin) < tolerance :
                mid == 1 ? Math.Abs(d.vertice.y - hMin) < tolerance :
                Math.Abs(d.vertice.z - hMin) < tolerance);
            var hMaxVer = outlines.FindAll(d => 
                mid == 0 ? Math.Abs(d.vertice.x - hMax) < tolerance : 
                mid == 1 ? Math.Abs(d.vertice.y - hMax) < tolerance :
                Math.Abs(d.vertice.z - hMax) < tolerance);
            // 流れ方向の最大最小取得
            var lMin = hMinVer.Min(d => max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z);
            var lMax = hMinVer.Max(d => max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z);
            straightLength = lMax - lMin;
            // 直線部を削除
            outlines = outlines.FindAll(d =>
                max == 0 ? (d.vertice.x >= lMax) || (d.vertice.x <= lMin) :
                max == 1 ? (d.vertice.y >= lMax) || (d.vertice.y <= lMin) :
                (d.vertice.z >= lMax) || (d.vertice.z <= lMin));
            hMinVer = hMinVer.FindAll(d =>
                max == 0 ? (d.vertice.x >= lMax) || (d.vertice.x <= lMin) :
                max == 1 ? (d.vertice.y >= lMax) || (d.vertice.y <= lMin) :
                (d.vertice.z >= lMax) || (d.vertice.z <= lMin)).OrderBy(d =>
                max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z).ToList();
            hMaxVer = hMaxVer.FindAll(d =>
                max == 0 ? (d.vertice.x >= lMax) || (d.vertice.x <= lMin) :
                max == 1 ? (d.vertice.y >= lMax) || (d.vertice.y <= lMin) :
                (d.vertice.z >= lMax) || (d.vertice.z <= lMin)).OrderBy(d =>
                max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z).ToList();
            // 中心算出
            center = new Vector3(
                max == 0 ? (lMax - lMin) / 2 + lMin : mid == 0 ? (hMax - hMin) / 2 + hMin : farthest.vertice.x,
                max == 1 ? (lMax - lMin) / 2 + lMin : mid == 1 ? (hMax - hMin) / 2 + hMin : farthest.vertice.y,
                max == 2 ? (lMax - lMin) / 2 + lMin : mid == 2 ? (hMax - hMin) / 2 + hMin : farthest.vertice.z
            );
            // 凸凹を無くす
            outlines = ConvexHull(outlines, max, mid);
            // 回転方向にソート
            outlines = isLinearRvs ?
                outlines.OrderBy(d =>
                {
                    double angle = Math.Atan2(
                        mid == 0 ? d.vertice.x - center.x : mid == 1 ? d.vertice.y - center.y : d.vertice.z - center.z,
                        max == 0 ? d.vertice.x - center.x : max == 1 ? d.vertice.y - center.y : d.vertice.z - center.z
                    );
                    if (angle < 0) angle += Math.PI * 2; // 0〜2πに正規化
                    return angle;
                }).ToList() :
                outlines.OrderByDescending(d =>
                {
                    double angle = Math.Atan2(
                        mid == 0 ? d.vertice.x - center.x : mid == 1 ? d.vertice.y - center.y : d.vertice.z - center.z,
                        max == 0 ? d.vertice.x - center.x : max == 1 ? d.vertice.y - center.y : d.vertice.z - center.z
                    );
                    if (angle < 0) angle += Math.PI * 2; // 0〜2πに正規化
                    return angle;
                }).ToList();
            var index = outlines.IndexOf(hMaxVer[0]);
            outlines = outlines.Skip(index).Concat(outlines.Take(index)).ToList();
            // 奥行方向をムーバーオブジェクトと合わせる
            linearSetting.gameObject.transform.parent = moveObject.transform;
            moverOffsetH = mid == 0 ? linearSetting.gameObject.transform.localPosition.x : mid == 1 ? linearSetting.gameObject.transform.localPosition.y : linearSetting.gameObject.transform.localPosition.z;
            var points = outlines.Select(d =>
            {
                var pos = new Vector3
                {
                    x = min == 0 ? linearSetting.gameObject.transform.localPosition.x : d.vertice.x,
                    y = min == 1 ? linearSetting.gameObject.transform.localPosition.y : d.vertice.y,
                    z = min == 2 ? linearSetting.gameObject.transform.localPosition.z : d.vertice.z
                };
                return pos;
            }
            ).ToList();
            //loopPathPoints.AddRange(points);
            loopPathPoints.AddRange(OffsetPath(points, moverOffsetH));
            loopPathPoints.Add(loopPathPoints[0]);
            // パスの総距離を計算
            calcLength = 0f;
            for (int i = 0; i < loopPathPoints.Count - 1; i++)
            {
                calcLength += Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);
            }
            // カーブ距離算出
            calcCurveLength = calcLength / 2 - straightLength;
        }
    }

    /// <summary>
    /// 平面上で凸包を計算
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    private List<VerticeInfo> ConvexHull(List<VerticeInfo> points, int max, int mid)
    {
        // X座標でソート
        var sorted = points
            .OrderBy(p => max == 0 ? p.vertice.x : max == 1 ? p.vertice.y : p.vertice.z)
            .ThenBy(p => mid == 0 ? p.vertice.x : mid == 1 ? p.vertice.y : p.vertice.z)
            .ToList();

        var hull = new List<VerticeInfo>();

        // 下側
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2].vertice, hull[hull.Count - 1].vertice, p.vertice, max, mid) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        // 上側
        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count >= lower && Cross(hull[hull.Count - 2].vertice, hull[hull.Count - 1].vertice, p.vertice, max, mid) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    float Cross(Vector3 o, Vector3 a, Vector3 b, int max, int mid)
    {
        var ax = max == 0 ? (a.x - o.x) : max == 1 ? (a.y - o.y) : (a.z - o.z);
        var bz = mid == 0 ? (b.x - o.x) : mid == 1 ? (b.y - o.y) : (b.z - o.z);
        var az = mid == 0 ? (a.x - o.x) : mid == 1 ? (a.y - o.y) : (a.z - o.z);
        var bx = max == 0 ? (b.x - o.x) : max == 1 ? (b.y - o.y) : (b.z - o.z);
        return ax * bz - az * bx;
    }

    /// <summary>
    /// パスを外側にする
    /// </summary>
    /// <param name="path"></param>
    /// <param name="offsetDistance"></param>
    /// <returns></returns>
    private List<Vector3> OffsetPath(List<Vector3> path, float offsetDistance)
    {
        var result = new List<Vector3>();

        for (int i = 0; i < path.Count; i++)
        {
            // 前後の点を取得（ループ対応）
            int prev = (i - 1 + path.Count) % path.Count;
            int next = (i + 1) % path.Count;

            // 前後の接線ベクトルを計算
            Vector3 dirPrev = (path[i] - path[prev]).normalized;
            Vector3 dirNext = (path[next] - path[i]).normalized;

            // 平均接線ベクトル
            Vector3 tangent = (dirPrev + dirNext).normalized;

            // XZ平面上の法線（接線を90度回転）
            Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x).normalized;

            // 外側にオフセット
            result.Add(path[i] + normal * offsetDistance);
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
        // 既存の動作オブジェクトを無効化
        linearSetting.gameObject.SetActive(false);
        if (loopPathPoints.Count > 4)
        {
            // バケット間隔
            moverPitch = linearSetting.pitch / 1000f;
            moverOffsetPos = linearSetting.offset / 1000f;
            totalLength = linearSetting.length / 1000f;
            curveLength = totalLength / 2 - straightLength;
            for (var i = 0; i < linearSetting.count; i++)
            {
                var mover = new MoverInfo
                {
                    id = i,
                    obj = Instantiate(linearSetting.gameObject),
                    pointno = -1,
                    pos = totalLength - moverPitch * i,
                    size = moverPitch,
                    length = totalLength
                };
                // パス上のその距離の位置を取得
                GetPositionOnPath(mover.pos, out Vector3 pos, out Vector3 dir);
                mover.obj.transform.parent = moveObject.transform.parent;
                mover.obj.transform.localPosition = pos;
                if (dir != Vector3.zero)
                {
                    // 円弧の中心からposへの方向を「上」として使う
                    Vector3 toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
                    Quaternion rot = Quaternion.LookRotation(dir, toPos);
                    mover.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                    if (i == 0)
                    {
                        // 初期オフセット
                        moverOffsetAng = mover.obj.transform.localEulerAngles - linearSetting.gameObject.transform.localEulerAngles;
                    }
                    mover.obj.transform.localEulerAngles -= moverOffsetAng;
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
        for (var i = 0; i < points.Count; i++)
        {
            var spd = linearSetting.spds.Count > points[i].spd ? linearSetting.spds[points[i].spd] : new LinearSetting.SpdInfo();
            var point = new PointInfo
            {
                id = this.points.Count,
                pos = points[i].pos / 1000f,
                totalLength = totalLength,
                actTag = points[i].tagAct,
                finTag = points[i].tagFin,
                type = points[i].type == "PP" ? PointType.PP : points[i].type == "BUFF" ? PointType.BUFF : PointType.TP,
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
            for (var j = 0; j < count; j++)
            {
                var pos = point.type == PointType.TP ? point.pos : (totalLength + (point.pos - moverPitch * j)) % totalLength;
                point.stopPoints.Add(new StopPoint
                {
                    pointId = this.points.Count,
                    stopId = j,
                    isPP = point.type == PointType.PP,
                    isTP = point.type == PointType.TP,
                    target = pos
                });
            }
            this.points.Add(point);
            // 通過ポイントならポイント到着後の終了ポイント作成
            if (point.type == PointType.TP)
            {
                // 通過開始点までの速度
                point.spdInfo.ve = point.spdInfo.vm;
                point.spdInfo.vm = point.spdInfo.vf;
                point.tpSpdInfo = new SpeedInfo
                {
                    vm = spd.vm / 1000f,
                    vf = spd.vm / 1000f,
                    ve = spd.vm / 1000f,
                    acl = spd.acl * 9.8f,
                    dcl = spd.dcl * 9.8f,
                    jerkA = spd.jerkA,
                    jerkD = spd.jerkD
                };
                // 到着後通過終了ポイント作成
                point = new PointInfo
                {
                    id = this.points.Count,
                    pos = points[i].pos / 1000f + points[i].wait / 1000f,
                    totalLength = totalLength,
                    actTag = points[i].tagAct,
                    finTag = points[i].tagFin,
                    type = PointType.TPE,
                    spdInfo = new SpeedInfo
                    {
                        vm = spd.ve / 1000f,
                        vf = spd.vf / 1000f,
                        acl = spd.acl * 9.8f,
                        dcl = spd.dcl * 9.8f,
                        jerkA = spd.jerkA,
                        jerkD = spd.jerkD,
                    }
                };
                for (var j = 0; j < count; j++)
                {
                    var pos = point.pos;
                    point.stopPoints.Add(new StopPoint
                    {
                        pointId = this.points.Count,
                        stopId = j,
                        isTPE = true,
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
            if (this.points[i].isTPE)
            {
                // 通過終了位置から次のポイントの最終速度は次の開始速度へ
                this.points[i].spdInfo.ve = this.points[i].next.spdInfo.vf;
            }
        }
        foreach(var point in this.points)
        {
            var p = point.next;
            for (var i = 0; i < this.points.Count; i++, p = p.next)
            {
                if ((p.type == PointType.PP) || (p.type == PointType.TP) || (p.type == PointType.TPE))
                {
                    point.nextPP = p;
                    break;
                }
            }
        }
        // 初期位置設定
        var initPoint = this.points.Find(d => d.isInit);
        var mover = movers[0];
        do
        {
            initPoint.SetMover(mover);
            mover = mover.next;
        } while (!mover.isHead);
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
    private void GetPositionOnPath(float distance, out Vector3 pos, out Vector3 dir)
    {
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
                if (!isLinearRvs)
                {
                    dir = -dir;
                }
                return;
            }
            accumulated += segLen;
        }
        // パスの終端
        pos = loopPathPoints[loopPathPoints.Count - 1];
        dir = (loopPathPoints[loopPathPoints.Count - 1] - loopPathPoints[loopPathPoints.Count - 2]).normalized;
        if (!isLinearRvs)
        {
            dir = -dir;
        }
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
        float maxDecel
    ){
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
        return table;
    }
    #endregion 速度カーブ作成
    #endregion 関数定義
}
