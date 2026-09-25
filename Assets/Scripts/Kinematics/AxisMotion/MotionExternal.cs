using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MotionExternal : AxisMotionBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// 動作タグ
    /// </summary>
    [SerializeField]
    protected TagInfo actTag;

    /// <summary>
    /// 現在の値
    /// </summary>
    [SerializeField]
    private int value;

    /// <summary>
    /// 比率
    /// </summary>
    protected float rate;

    /// <summary>
    /// 前回のタグ由来位置(mm)。折り返しの展開に使う
    /// </summary>
    private float prevBacketMm;

    /// <summary>
    /// 展開後の経路上の位置(mm)
    /// </summary>
    private float backetTravelMm;

    /// <summary>
    /// バケット位置を一度でも受け取ったか（初回は差分を取らない）
    /// </summary>
    private bool hasBacketMm;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();

        // ユニット設定更新
        RenewMoveDir();
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    public override void RenewMoveDir()
    {
        base.RenewMoveDir();
        rate = (float)unitSetting.actionSetting.rate;
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        if (!GlobalScript.isLoaded)
        {
            // ロード中は動かさない。
            // 子ユニットの親付け替え（拡張機構の動作端へ／取付先モデルへ）は
            // いずれもワールド姿勢を維持して行うため、付け替えた瞬間の親の姿勢が
            // そのままローカル位置として焼き付く。ここでロード中から書き込むと
            // 親の機構が既に動いた状態で付け替えられ、子の取り付け位置がずれる
            // （offset を持つユニットはタグ値0でも即座にその分動くため必ず起きる）。
            // 内部動作は動作が発火するまで書かないのでこの問題が出ない。
            // 内部と外部で挙動を揃える意味でもロード完了まで待つ
            return;
        }
        if (!isManual)
        {
            value = GetTagValue(unitSetting.actionSetting.tag, ref actTag);
        }
        if (isBacket)
        {
            // バケットは moveObject を直接動かさず、経路上の位置(mm)で爪を進める。
            // 単位は直動側が value/rate をメートルとして扱うのに合わせ Thousand を掛ける
            // （rate は1メートルあたりのカウント数。KMXToolの 1μm→1000000 等）。
            // 動作方向に dir は掛けない。バケットは基本が正転で、進行方向は値の増減が表す。
            // 掛けると値列の増減が反転してしまう
            var mm = value / (rate == 0 ? 1000f : rate) * Thousand
                + unitSetting.actionSetting.offset;
            var loopMm = BacketLoopMm;
            if (!hasBacketMm)
            {
                hasBacketMm = true;
                backetTravelMm = mm;
            }
            else
            {
                // タグはバケット長で折り返す絶対位置（実機ではサーボの現在位置）。
                // 半周を超える飛びだけを折り返しとみなして展開する。
                // それ未満の戻りはサーボのゲインによる揺れ（±数カウント）なので
                // そのまま戻す＝実機と同じくその場で揺れる。
                // ※ MoveBacket のリセット検出は差分の符号だけを見て大きさを問わないため、
                //   生の値を渡すと揺れのたびに経路上をワープしてしまう
                var delta = mm - prevBacketMm;
                if (loopMm > 0.001f)
                {
                    if (delta < -loopMm * 0.5f)
                    {
                        delta += loopMm;
                    }
                    else if (delta > loopMm * 0.5f)
                    {
                        delta -= loopMm;
                    }
                }
                backetTravelMm += delta;
                if (loopMm > 0.001f)
                {
                    // 長時間運転でも精度が落ちないよう周長で正規化する（位置は剰余で決まるので挙動は不変）
                    backetTravelMm %= loopMm;
                    if (backetTravelMm < 0f)
                    {
                        backetTravelMm += loopMm;
                    }
                }
            }
            prevBacketMm = mm;
            SetBacketPosition(backetTravelMm);
            return;
        }
        var data = moveDir * unitSetting.actionSetting.dir * value / (rate == 0 ? 1000f : rate) + (moveDir * unitSetting.actionSetting.offset / (isRotate ? 1f : 1000f));
        if (isRotate)
        {
            moveObject.transform.localEulerAngles = data;
            if (chuckSetting != null)
            {
                foreach (var child in chuckSetting.children)
                {
                    child.setting.moveObject.transform.localEulerAngles = moveObject.transform.localEulerAngles * child.dir * child.rate + child.offset * moveDir;
                }
            }
        }
        else
        {
            moveObject.transform.localPosition = data;
            if (chuckSetting != null)
            {
                foreach (var child in chuckSetting.children)
                {
                    child.setting.moveObject.transform.localPosition = moveObject.transform.localPosition * child.dir * child.rate + child.offset * moveDir / Thousand;
                }
            }
        }
    }
}
