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
        if (!isManual)
        {
            value = GetTagValue(unitSetting.actionSetting.tag, ref actTag);
        }
        if (isBacket)
        {
            // バケットは moveObject を直接動かさず、経路上の送り量(mm)で爪を進める。
            // タグ値はバケット長の範囲で折り返すが（100→10 は逆転ではなく110へ前進）、
            // MoveBacket が「値が戻ったら方向転換ではなく駆動値リセット」として
            // それまでの位置を積算に繰り入れるため、生の値をそのまま渡してよい。
            // 単位は同期スレーブ側（BacketTravelMm * syncRate * syncDir + syncOffset）と同じスカラのmm。
            // 直動側が value/rate をメートルとして扱うので、mmへは Thousand を掛ける
            // （rate は1メートルあたりのカウント数。KMXToolの 1μm→1000000 等）。
            // 動作方向は dir を掛けない。バケットは基本が正転で、進行方向は
            // MoveBacket が前回値との差分から判断する。ここで dir を掛けると
            // 値列の増減が反転し、毎回「逆転」と判定されてしまう
            var travelMm = value / (rate == 0 ? 1000f : rate) * Thousand
                + unitSetting.actionSetting.offset;
            MoveBacket(travelMm);
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
