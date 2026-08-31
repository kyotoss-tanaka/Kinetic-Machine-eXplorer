using KssColliderHullReducer;
using NUnit.Framework;
using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// レバー機構
/// </summary>
public class ExMechParallelLinkInfo : ExMechInfo
{
    /// <summary>
    /// ダブル並行リンク
    /// </summary>
    public bool isDouble;

    /// <summary>
    /// オフセット
    /// </summary>
    public List<Vector3> offsets = new();

    /// <summary>
    /// 回転方向
    /// </summary>
    public List<Vector3> dirs = new();

    /// <summary>主軸の初期姿勢（差分角の基準）</summary>
    private Quaternion initMainRot;

    /// <summary>アーム2・プレートの初期ローカル姿勢（軸回転の基準）</summary>
    private Quaternion initRotA1;
    private Quaternion initRotA4;

    /// <summary>四節リンク解析用（アーム2の回転面の法線と面内基底・半径・前回解）</summary>
    private Vector3 linkN;
    private Vector3 linkU;
    private Vector3 linkV;
    private float linkR;
    private Vector2 lastJ2;
    private Quaternion initWorldRotA1;
    private bool linkSolverReady;

    /// <summary>アーム1の初期方向（回転中心→リンク1）と初期姿勢。向きの追従計算に使う</summary>
    private Vector3 initDir1;
    private Quaternion initRot1;

    /// <summary>アーム3の初期方向（回転中心→リンク2）と初期姿勢（ダブル構成）</summary>
    private Vector3 initDir3;
    private Quaternion initRot3;

    /// <summary>
    /// 制御対象オブジェクト
    /// </summary>
    public GameObject pntObj0;
    public GameObject pntObj2_0;
    public GameObject pntObj2_1;
    public GameObject pntObj3;
    public GameObject pntObj4;
    public GameObject pntObj5_3;
    public GameObject pntObj5_4;

    /// <summary>
    /// 初期化
    /// </summary>
    public override void Initialize()
    {
        base.Initialize();

        // 制御対象オブジェクトを作成
        // ※位置はroot（回転中心指定があればピボット）で取る。RenewPosがrootをこの点へ動かすため、
        //   モデル原点で取ると回転中心指定モデルが初期状態からズレる
        pntObj0 = new GameObject("Point0");
        pntObj0.transform.parent = mainAxis.model.transform;
        pntObj0.transform.position = axisInfos[0].root.position;
        pntObj2_0 = new GameObject("Point2_0");
        pntObj2_0.transform.parent = axisInfos[0].model.transform;
        pntObj2_0.transform.position = axisInfos[2].root.position;
        pntObj2_1 = new GameObject("Point2_1");
        pntObj2_1.transform.parent = axisInfos[1].model.transform;
        pntObj2_1.transform.position = axisInfos[2].root.position;
        if (isDouble)
        {
            pntObj3 = new GameObject("Point3");
            pntObj3.transform.parent = axisInfos[1].model.transform;
            pntObj3.transform.position = axisInfos[3].root.position;
            pntObj4 = new GameObject("Point4");
            pntObj4.transform.parent = axisInfos[0].model.transform;
            pntObj4.transform.position = axisInfos[4].root.position;
            pntObj5_3 = new GameObject("Point5_3");
            pntObj5_3.transform.parent = axisInfos[3].model.transform;
            pntObj5_3.transform.position = axisInfos[5].root.position;
            pntObj5_4 = new GameObject("Point5_4");
            pntObj5_4.transform.parent = axisInfos[4].model.transform;
            pntObj5_4.transform.position = axisInfos[5].root.position;
        }
        // 主軸の初期姿勢（以後は初期姿勢からの差分回転角で従動側を回す）
        initMainRot = mainAxis.model.transform.localRotation;
        initRotA1 = axisInfos[1].root.localRotation;
        if (isDouble && (axisInfos[4].model != null))
        {
            initRotA4 = axisInfos[4].root.localRotation;
        }

        // アーム1の向きの基準（回転中心→リンク1の初期方向）
        if (axisInfos[2].model != null)
        {
            initDir1 = axisInfos[2].root.position - axisInfos[0].root.position;
            initRot1 = axisInfos[0].root.rotation;
        }
        if (isDouble && (axisInfos[5].model != null))
        {
            initDir3 = axisInfos[5].root.position - axisInfos[3].root.position;
            initRot3 = axisInfos[3].root.rotation;
        }

        // 回転方向取得と初期角度オフセット（回転を適用するroot基準）
        for (var i = 0; i < axisInfos.Count; i++)
        {
            offsets.Add(new());
            dirs.Add(new());
            if (axisInfos[i].model != null)
            {
                dirs[i] = GetRotationAxis(axisInfos[i].root);
                offsets[i] = axisInfos[i].root.localEulerAngles;
            }
        }

        // 四節リンク解析の準備（アーム2の回転面内で、リンク1連結点を円と円の交点で解く）
        if (axisInfos[2].model != null)
        {
            var c = axisInfos[1].root.position;
            var j0 = axisInfos[2].root.position - c;
            linkN = (axisInfos[1].root.rotation * dirs[1]).normalized;
            linkU = Vector3.ProjectOnPlane(j0, linkN).normalized;
            linkV = Vector3.Cross(linkN, linkU);
            linkR = Vector3.ProjectOnPlane(j0, linkN).magnitude;
            lastJ2 = new Vector2(Vector3.Dot(j0, linkU), Vector3.Dot(j0, linkV));
            initWorldRotA1 = axisInfos[1].root.rotation;
            linkSolverReady = (linkR > 1e-6f) && (initDir1.magnitude > 1e-6f);
        }
    }

    /// <summary>
    /// 動作軸取得
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public Vector3 GetRotationAxis(Transform obj)
    {
        var pnt0 = obj.InverseTransformPoint(mainAxis.model.transform.TransformPoint(Vector3.zero));
        var pnt1 = obj.InverseTransformPoint(mainAxis.model.transform.TransformPoint(mainDir));
        // 正規化する（スケールの掛かったモデルでは長さが1にならず、軸判定が全て不成立になる）
        var ret = (pnt1 - pnt0).normalized;
        return ret;
    }

    /// <summary>
    /// 有効角度取得
    /// </summary>
    /// <param name="point"></param>
    /// <param name="dir"></param>
    /// <returns></returns>
    public float GetMaskAngle(Vector3 point, Vector3 dir)
    {
        var tmp = Vector3.Scale(point, dir);
        return tmp.x + tmp.y + tmp.z;
    }

    /// <summary>
    /// 位置更新
    /// </summary>
    public override void RenewPos()
    {
        base.RenewPos();

        // 主軸の初期姿勢からの実回転角（オイラー角の1成分だと回転軸が親ローカル軸とズレた機械で角度が一致しない）
        var dq = Quaternion.Inverse(initMainRot) * mainAxis.model.transform.localRotation;
        dq.ToAngleAxis(out var dAng, out var dAxis);
        if (dAng > 180f)
        {
            dAng -= 360f;
        }
        // 回転方向の符号は主軸方向との内積で決める
        var ang = dAng * Mathf.Sign(Vector3.Dot(dAxis, mainDir));

        // アーム1の位置（主軸連結点に追従。回転中心=root）
        axisInfos[0].root.position = pntObj0.transform.position;
        var rodLen = initDir1.magnitude;
        if (linkSolverReady)
        {
            // 四節リンク解析：アーム2の回転面内で、
            // 「アーム2中心から半径linkR（アーム2長）の円」と「アーム1回転中心（クランクピン）から半径ロッド長の円」の交点＝連結点を解く
            // （主軸と同角度回転では、腕の長さが等しい平行四辺形以外でロッドが伸び縮みしてしまう）
            var c = axisInfos[1].root.position;
            var pv = axisInfos[0].root.position - c;
            var p2 = new Vector2(Vector3.Dot(pv, linkU), Vector3.Dot(pv, linkV));
            var d = p2.magnitude;
            if (d > 1e-6f)
            {
                var a = (d * d + linkR * linkR - rodLen * rodLen) / (2f * d);
                var h2 = linkR * linkR - a * a;
                var h = h2 > 0f ? Mathf.Sqrt(h2) : 0f;   // 交点なし（リンク伸び切り）は直線状とみなす
                var basePnt = p2 * (a / d);
                var perp = new Vector2(-p2.y, p2.x) / d;
                var j1 = basePnt + perp * h;
                var j2 = basePnt - perp * h;
                // 前回解に近い側を採用（枝の連続性）
                var J = (Vector2.Distance(j1, lastJ2) <= Vector2.Distance(j2, lastJ2)) ? j1 : j2;
                lastJ2 = J;
                // アーム2角度＝初期連結点（角度0）からの回転角
                var th = Mathf.Atan2(J.y, J.x) * Mathf.Rad2Deg;
                axisInfos[1].root.rotation = Quaternion.AngleAxis(th, linkN) * initWorldRotA1;
            }
        }
        else
        {
            // リンク1未登録時：主軸と同角度回転（従来の平行リンク動作）＋連結点一致チェックで反転
            axisInfos[1].root.localRotation = initRotA1 * Quaternion.AngleAxis(-ang, dirs[1]);
            if (Vector3.Distance(pntObj2_0.transform.position, pntObj2_1.transform.position) > 0.001f)
            {
                axisInfos[1].root.localRotation = initRotA1 * Quaternion.AngleAxis(ang, dirs[1]);
            }
        }
        if (axisInfos[2].model != null)
        {
            // リンク1（連結部品）はアーム2側の連結点へ追従（アーム2が動けば付いてくる）
            axisInfos[2].root.position = pntObj2_1.transform.position;
            // アーム1の向き＝「アーム1の回転中心→リンク1」の方向で決める
            var nowDir = pntObj2_1.transform.position - axisInfos[0].root.position;
            if ((initDir1 != Vector3.zero) && (nowDir != Vector3.zero))
            {
                axisInfos[0].root.rotation = Quaternion.FromToRotation(initDir1, nowDir) * initRot1;
            }
        }
        if (isDouble)
        {
            axisInfos[3].root.position = pntObj3.transform.position;
            // プレートは自軸で主軸と同角度回転（軸回転で適用。回転方向はアーム3回転中心⇔リンク2の距離＝ロッド長が保たれる側）
            var rod3Len = initDir3.magnitude;
            axisInfos[4].root.localRotation = initRotA4 * Quaternion.AngleAxis(-ang, dirs[4]);
            axisInfos[4].root.position = pntObj4.transform.position;
            if ((axisInfos[5].model != null) && (rod3Len > 0f))
            {
                var errM = Mathf.Abs(Vector3.Distance(pntObj5_4.transform.position, axisInfos[3].root.position) - rod3Len);
                if (errM > 0.001f)
                {
                    axisInfos[4].root.localRotation = initRotA4 * Quaternion.AngleAxis(ang, dirs[4]);
                    var errP = Mathf.Abs(Vector3.Distance(pntObj5_4.transform.position, axisInfos[3].root.position) - rod3Len);
                    if (errM < errP)
                    {
                        axisInfos[4].root.localRotation = initRotA4 * Quaternion.AngleAxis(-ang, dirs[4]);
                    }
                }
            }
            else if (Vector3.Distance(pntObj5_3.transform.position, pntObj5_4.transform.position) > 0.001f)
            {
                // リンク2未登録時は従来の連結点一致チェック
                axisInfos[4].root.localRotation = initRotA4 * Quaternion.AngleAxis(ang, dirs[4]);
            }
            if (axisInfos[5].model != null)
            {
                // リンク2（連結部品）はプレート側の連結点へ追従
                axisInfos[5].root.position = pntObj5_4.transform.position;
                // アーム3の向き＝「アーム3の回転中心→リンク2」の方向で決める
                var nowDir = pntObj5_4.transform.position - axisInfos[3].root.position;
                if ((initDir3 != Vector3.zero) && (nowDir != Vector3.zero))
                {
                    axisInfos[3].root.rotation = Quaternion.FromToRotation(initDir3, nowDir) * initRot3;
                }
            }
        }
    }

    /// <summary>
    /// 次の角度を取得
    /// </summary>
    /// <param name="offset"></param>
    /// <param name="ang"></param>
    /// <param name="dir"></param>
    /// <returns></returns>
    private Vector3 GetNextAngle(Vector3 offset, float ang, Vector3 dir)
    {
        // 成分が最大の軸を回転軸とする（完全一致の比較だと軸が僅かに傾いたモデルで判定できない）
        var ax = Mathf.Abs(dir.x);
        var ay = Mathf.Abs(dir.y);
        var az = Mathf.Abs(dir.z);
        if ((ax >= ay) && (ax >= az))
        {
            offset.x -= ang;
        }
        else if (ay >= az)
        {
            offset.y -= ang;
        }
        else
        {
            offset.z -= ang;
        }
        return offset;
    }
}
