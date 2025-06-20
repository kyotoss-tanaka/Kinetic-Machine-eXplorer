using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExternalController : AxisMotionBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    protected TagInfo actTag;

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

        // 動作タグ設定
        actTag = ScriptableObject.CreateInstance<TagInfo>();
        actTag.Database = unitSetting.Database;
        actTag.MechId = unitSetting.mechId;
        actTag.Tag = unitSetting.actionSetting.tag;
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        var data = GlobalScript.GetTagData(actTag);
        if (isRotate)
        {
            moveObject.transform.localEulerAngles = moveDir * data;
        }
        else
        {
            moveObject.transform.localPosition = moveDir * data;
        }
    }
}
