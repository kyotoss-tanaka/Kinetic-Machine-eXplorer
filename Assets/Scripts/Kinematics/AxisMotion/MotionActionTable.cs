using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public class MotionActionTable : AxisMotionBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// データ倍率
    /// </summary>
    protected float rate;

    /// <summary>
    /// 手動設定
    /// </summary>
    [SerializeField]
    protected bool isManual;

    /// <summary>
    /// 現在時間
    /// </summary>
    [SerializeField]
    protected int time;

    /// <summary>
    /// 現在サイクル
    /// </summary>
    [SerializeField]
    protected int cycle;

    /// <summary>
    /// テーブル位置
    /// </summary>
    [SerializeField]
    protected decimal value;

    /// <summary>
    /// 現在位置
    /// </summary>
    [SerializeField]
    protected float position;

    /// <summary>
    /// オフセット
    /// </summary>
    [SerializeField]
    protected int offset;

    /// <summary>
    /// 動作テーブル
    /// </summary>
    [SerializeField]
    protected ActionTableData? actionTableData;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();

        // ユニット設定更新
        renewUnitSetting();
    }

    /// <summary>
    /// ユニット設定から動作設定更新
    /// </summary>
    protected override void renewUnitSetting()
    {
        base.renewUnitSetting();

        // サイクルタグ設定
        var tag = GlobalScript.callbackTags.Find(d => d.database == unitSetting.Database);
        cycleTag = tag == null ? null : tag.cycle;

        rate = (float)unitSetting.actionSetting.rate;

        offset = unitSetting.actionSetting.offset;

        // テーブル取得
        actionTableData = GlobalScript.actionTableDatas.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
        // 時間ごとにソート
        actionTableData.datas = actionTableData.datas.OrderBy(d => d.time).ToList();
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {  
        time = GlobalScript.GetTagData(cycleTag);
        if (!isManual)
        {
            cycle = time % unitSetting.actionSetting.cycle;
        }
        if ((actionTableData != null) && (actionTableData.datas.Count > 0))
        {
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
        }
    }
}
