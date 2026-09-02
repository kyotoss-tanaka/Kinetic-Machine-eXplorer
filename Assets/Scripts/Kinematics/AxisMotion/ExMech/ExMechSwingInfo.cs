using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 揺動機構：直動軸（シリンダ/直動モータ）の伸縮で揺動アームを振る機構。
/// 直動軸の根本が固定ピボット（首振り）、先端（リンク=連結点）が固定回転軸を持つアームに連結される。
/// 幾何解: 「本体回転中心Aから半径L（初期軸長＋ストローク）の円」と「アーム回転中心Cから半径R（アーム長）の円」の
/// 交点＝連結点J を解き、アーム=C→J方向、本体/ロッド=A→J方向に配置する。
/// スロット: axisInfos[0]=直動軸本体, [1]=揺動アーム, [2]=リンク（連結部品）。主軸(mainAxis)=直動軸の可動側（ロッド）。
/// 駆動: 動作テーブルの直動＋拡張機構モード変更（SetMovePosで目標ローカル位置を受ける）。
/// </summary>
public class ExMechSwingInfo : ExMechInfo
{

    private Vector3 pntA;        // 直動軸本体の回転中心（初期・ワールド。軸判定と定数算出用）
    private Vector3 pntC;        // 揺動アームの回転中心（初期・ワールド）
    private Vector3 dir0;        // 初期の軸方向（A→初期連結点J0）
    private float len0;          // 初期の軸長 |A→J0|（3D）
    private Vector3 planeN;      // アームの回転軸（初期・ワールド）
    // アーム角θの厳密解用（連結点J(θ)=C+R(θ)v0 と |J(θ)-A|=軸長 を直接解く。並行リンクと同じ「実回転軸まわりの回転」方式）
    // ※これらは長さ・内積のみに使うため、機械が剛体移動しても値は不変
    private Vector3 v0;          // C→初期連結点
    private Vector3 vPar;        // v0の軸方向成分
    private Vector3 vPerp;       // v0の軸直交成分
    private Vector3 nCrossVPerp; // 軸×v0直交成分
    private Vector3 wCA;         // A→C（固定）
    private float lastTheta;     // 前回のアーム角（枝の連続性用）
    private bool solverReady;

    // 親ユニットの動きに追従するため、幾何はユニット追従フレーム（workSpace）ローカルで保持し、毎フレームワールドへ変換する
    private Transform frame;         // 基準フレーム（workSpace。ユニットと一緒に動く）
    private Vector3 aL;              // 本体回転中心（フレームローカル）
    private Vector3 cL;              // アーム回転中心（フレームローカル）
    private Vector3 v0L;             // C→初期連結点（フレームローカル）
    private Vector3 dir0L;           // 初期軸方向（フレームローカル）
    private Vector3 nL;              // 回転軸（フレームローカル）
    private Vector3 rodPosL;         // ロッド初期位置（フレームローカル）
    private Quaternion rotBodyRel;   // フレーム基準の初期姿勢（本体/アーム/ロッド/リンク）
    private Quaternion rotArmRel;
    private Quaternion rotRodRel;
    private Quaternion rotLinkRel;

    /// <summary>各スロットにモデル本体が登録されているか（falseなら回転中心指定の位置参照のみで、モデルは動かさない）</summary>
    private bool hasBodyModel;
    private bool hasArmModel;
    private bool hasLinkModel;

    /// <summary>
    /// スロットの基準点を取得する。モデル本体があればroot（回転中心指定があればその中心）、
    /// モデル未登録なら回転中心指定部品の「原点」（位置参照のみ＝モデルは動かさない）。
    /// ※位置参照のみの場合、ロッドエンド等の非対称部品ではバウンズ中心が関節中心とずれるため原点を使う。
    /// 　原点が関節中心にあるノードを参照すること（Ctrl+クリック選択で出る軸表示の位置＝原点）。
    /// </summary>
    private static bool TryGetSlotPoint(ExMechAxisInfo axis, out Vector3 point, out bool hasModel)
    {
        if ((axis != null) && (axis.model != null))
        {
            point = axis.root.position;
            hasModel = true;
            return true;
        }
        if ((axis != null) && (axis.pivotSource != null))
        {
            point = axis.pivotSource.transform.position;
            hasModel = false;
            return true;
        }
        point = Vector3.zero;
        hasModel = false;
        return false;
    }

    /// <summary>警告ログの抑制用（1秒ごと）</summary>
    private float lastLogTime = float.MinValue;

    /// <summary>
    /// 初期化処理
    /// </summary>
    public override void Initialize()
    {
        if (axisInfos.Count < 3)
        {
            Debug.LogWarning($"揺動機構: 直動軸本体/揺動アーム/リンクの3スロットが必要です");
            return;
        }
        if (!exModeChange)
        {
            Debug.LogWarning($"揺動機構: 動作設定の「拡張機構モード変更」がOFFです（直動指令が機構に届きません）");
        }
        // 駆動値（ローカル位置）の基準
        initExPos = mainAxis.model.transform.localPosition;

        // 各スロットの基準点（モデル本体のroot、またはモデル未登録なら回転中心指定部品の中心＝位置参照のみ）。
        // 位置参照のみのスロットは、他ユニット・他機構が管理しているモデルの回転中心だけを借り、モデルは一切動かさない
        if (!TryGetSlotPoint(axisInfos[0], out pntA, out hasBodyModel))
        {
            Debug.LogWarning($"揺動機構: 直動軸本体のモデル、または子グリッドの回転中心指定が必要です");
            return;
        }
        if (!TryGetSlotPoint(axisInfos[1], out pntC, out hasArmModel))
        {
            Debug.LogWarning($"揺動機構: 揺動アームのモデル、または子グリッドの回転中心指定が必要です");
            return;
        }
        if (!TryGetSlotPoint(axisInfos[2], out var j0, out hasLinkModel))
        {
            Debug.LogWarning($"揺動機構: リンクのモデル、または子グリッドの回転中心指定が必要です");
            return;
        }
        if (!hasBodyModel || !hasArmModel || !hasLinkModel)
        {
            Debug.Log($"揺動機構: 位置参照のみのスロット → 本体={!hasBodyModel} アーム={!hasArmModel} リンク={!hasLinkModel}（該当スロットのモデルは動かさない）");
        }
        var axis0 = j0 - pntA;
        len0 = axis0.magnitude;
        dir0 = len0 > 1e-9f ? axis0 / len0 : Vector3.forward;

        // 回転軸の決定：本体回転中心・アーム回転中心・連結点（リンク）の3点は揺動面内に並ぶため、
        // 3点の座標の「変化量が最も少ない軸」＝揺動面の法線＝回転軸となる。
        // さらに主軸の動作方向成分が大きい軸は除外（回転軸は動作方向と直交）。
        // 採用した軸は純軸（成分1・他0）にスナップする（バウンズ中心の計測オフセットの影響を受けないように）。
        var spread = new Vector3(
            Mathf.Max(pntA.x, Mathf.Max(pntC.x, j0.x)) - Mathf.Min(pntA.x, Mathf.Min(pntC.x, j0.x)),
            Mathf.Max(pntA.y, Mathf.Max(pntC.y, j0.y)) - Mathf.Min(pntA.y, Mathf.Min(pntC.y, j0.y)),
            Mathf.Max(pntA.z, Mathf.Max(pntC.z, j0.z)) - Mathf.Min(pntA.z, Mathf.Min(pntC.z, j0.z)));
        var maxSpread = Mathf.Max(spread.x, Mathf.Max(spread.y, spread.z));
        if (maxSpread < 1e-6f)
        {
            Debug.LogWarning($"揺動機構: 3つの回転中心が同一点にあり回転軸を決定できません");
            return;
        }
        // スコア＝変化量（正規化）＋主軸方向成分。最小の軸を回転軸に採用
        var score = new Vector3(
            spread.x / maxSpread + Mathf.Abs(dir0.x),
            spread.y / maxSpread + Mathf.Abs(dir0.y),
            spread.z / maxSpread + Mathf.Abs(dir0.z));
        string normalSrc;
        if ((score.x <= score.y) && (score.x <= score.z))
        {
            planeN = Vector3.right;
            normalSrc = "X軸";
        }
        else if (score.y <= score.z)
        {
            planeN = Vector3.up;
            normalSrc = "Y軸";
        }
        else
        {
            planeN = Vector3.forward;
            normalSrc = "Z軸";
        }
        // θ解析用の分解（投影は使わず、実際の回転円 J(θ)=C+R(θ)v0 で解く）
        v0 = j0 - pntC;
        vPar = planeN * Vector3.Dot(v0, planeN);
        vPerp = v0 - vPar;
        nCrossVPerp = Vector3.Cross(planeN, vPerp);
        wCA = pntC - pntA;
        lastTheta = 0f;
        if (vPerp.sqrMagnitude < 1e-10f)
        {
            Debug.LogWarning($"揺動機構: 連結点がアーム回転軸上にあり解けません（リンク/アーム回転中心の指定を確認してください）");
            return;
        }

        // 幾何・初期姿勢をユニット追従フレーム（workSpace）ローカルで保持する
        // （親ユニットに載っている場合、ワールド固定だと親の動きを打ち消してしまうため）
        frame = workSpace.transform;
        aL = frame.InverseTransformPoint(pntA);
        cL = frame.InverseTransformPoint(pntC);
        v0L = frame.InverseTransformVector(v0);
        dir0L = frame.InverseTransformDirection(dir0);
        nL = frame.InverseTransformDirection(planeN);
        rodPosL = frame.InverseTransformPoint(mainAxis.model.transform.position);
        var frameRotInv = Quaternion.Inverse(frame.rotation);
        rotBodyRel = hasBodyModel ? frameRotInv * axisInfos[0].root.rotation : Quaternion.identity;
        rotArmRel = hasArmModel ? frameRotInv * axisInfos[1].root.rotation : Quaternion.identity;
        rotLinkRel = hasLinkModel ? frameRotInv * axisInfos[2].root.rotation : Quaternion.identity;
        rotRodRel = frameRotInv * mainAxis.model.transform.rotation;

        solverReady = len0 > 1e-6f;
        Debug.Log($"揺動機構: [{workSpace.transform.parent.name}] 初期化 回転軸={normalSrc} 軸長={len0 * 1000f:F1}mm アーム半径={vPerp.magnitude * 1000f:F1}mm 本体中心={pntA} アーム中心={pntC}");
    }

    /// <summary>
    /// 位置更新処理
    /// </summary>
    public override void RenewPos()
    {
        if (!solverReady)
        {
            if (Time.unscaledTime - lastLogTime > 1f)
            {
                lastLogTime = Time.unscaledTime;
                Debug.LogWarning($"揺動機構: ソルバ未準備のため停止中（初期化ログ/警告を確認してください）");
            }
            return;
        }
        // 現在のフレーム姿勢からワールドの基準点・軸を再構成する（親ユニットの動きに追従）
        var A = frame.TransformPoint(aL);
        var C = frame.TransformPoint(cL);
        var n = frame.TransformDirection(nL);
        var dir0w = frame.TransformDirection(dir0L);

        // ストローク（初期位置からの直動量・ワールドm。初期軸方向への射影なので符号も自動で決まる）
        var stroke = 0f;
        if (hasMovePos)
        {
            var parent = mainAxis.model.transform.parent;
            var deltaLocal = moveExPos - initExPos;
            var world = parent != null ? parent.TransformVector(deltaLocal) : deltaLocal;
            stroke = Vector3.Dot(world, dir0w);
        }
        // アーム角θの厳密解：連結点 J(θ)=C+R(θ)v0 が |J(θ)-A|=軸長 を満たすθを解く（投影なし・3Dのまま）
        // |J-A|² = |v0|² + |w|² + 2 w・R(θ)v0,  w=C-A
        // w・R(θ)v0 = w・v∥ + cosθ(w・v⊥) + sinθ(w・(n×v⊥)) → αcosθ + βsinθ = k の標準形
        var len3d = len0 + stroke;
        var alpha = Vector3.Dot(wCA, vPerp);
        var beta = Vector3.Dot(wCA, nCrossVPerp);
        var k = (len3d * len3d - v0.sqrMagnitude - wCA.sqrMagnitude) / 2f - Vector3.Dot(wCA, vPar);
        var mag = Mathf.Sqrt(alpha * alpha + beta * beta);
        if (mag < 1e-9f)
        {
            return;
        }
        var ratio = Mathf.Clamp(k / mag, -1f, 1f);   // 範囲外（リンク伸び切り）は端でクランプ
        var phi = Mathf.Atan2(beta, alpha) * Mathf.Rad2Deg;
        var delta = Mathf.Acos(ratio) * Mathf.Rad2Deg;
        var th1 = phi + delta;
        var th2 = phi - delta;
        // 前回角に近い側を採用（枝の連続性）
        var th = (Mathf.Abs(Mathf.DeltaAngle(lastTheta, th1)) <= Mathf.Abs(Mathf.DeltaAngle(lastTheta, th2))) ? th1 : th2;
        th = lastTheta + Mathf.DeltaAngle(lastTheta, th);
        lastTheta = th;
        var rot = Quaternion.AngleAxis(th, n);
        var J = frame.TransformPoint(cL + Quaternion.AngleAxis(th, nL) * v0L);

        // アーム：回転軸まわりの回転（初期姿勢=角度0）※位置参照のみのスロットは動かさない
        if (hasArmModel)
        {
            axisInfos[1].root.rotation = rot * (frame.rotation * rotArmRel);
        }

        // 本体・ロッド：Aを中心に軸をA→Jへ向ける。ロッドはさらに軸方向へストローク分並進
        var dirNow = (J - A).normalized;
        var q = Quaternion.FromToRotation(dir0w, dirNow);
        if (hasBodyModel)
        {
            axisInfos[0].root.rotation = q * (frame.rotation * rotBodyRel);
        }
        mainAxis.model.transform.rotation = q * (frame.rotation * rotRodRel);
        mainAxis.model.transform.position = A + q * (frame.TransformPoint(rodPosL) - A) + dirNow * stroke;

        // リンク（連結部品）：連結点に配置（姿勢はアームと共回り。アーム先端に付いて一緒に振れる部品）
        // ※位置参照のみ（モデル未登録・回転中心指定だけ）の場合は動かさない
        if (hasLinkModel)
        {
            axisInfos[2].root.rotation = rot * (frame.rotation * rotLinkRel);
            axisInfos[2].root.position = J;
        }

        // 表示用
        nowPos = new Vector3(stroke * 1000f, 0, 0);
        nowAngle = new Vector3(0, 0, th);

    }
}
