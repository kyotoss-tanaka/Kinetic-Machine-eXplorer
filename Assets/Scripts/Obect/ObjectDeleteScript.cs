using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectDeleteScript : KinematicsBase
{
    [SerializeField]
    private TagInfo Tag;

    [SerializeField]
    private float deleteDistance;

    [SerializeField]
    private Vector3 deletePos;

    /// <summary>
    /// 設定
    /// </summary>
    protected WorkDeleteSetting wkDeleteSetting;

    /// <summary>
    /// 前回のクリアフラグ
    /// </summary>
    private bool isClear = false;

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();

        var clear = GlobalScript.GetTagData(Tag) == 1;
        if (clear && !isClear)
        {
            // クリアフラグON
            float dis = Vector3.Distance(transform.localPosition, deletePos);
            if (dis < deleteDistance)
            {
                var dels = GetComponentsInChildren<ObjectScript>();
                foreach (var del in dels)
                {
                    Destroy(del.gameObject);
                }
            }
        }
        isClear = clear;
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        wkDeleteSetting = (WorkDeleteSetting)obj;
        Tag = ScriptableObject.CreateInstance<TagInfo>();
        Tag.Database = unitSetting.Database;
        Tag.MechId = unitSetting.mechId;
        Tag.Tag = wkDeleteSetting.tag;
        deleteDistance = wkDeleteSetting.distance;
        deletePos = new Vector3
        {
            x = wkDeleteSetting.pos[0] * transform.localScale.x,
            y = wkDeleteSetting.pos[1] * transform.localScale.y,
            z = wkDeleteSetting.pos[2] * transform.localScale.z
        };
    }
}
