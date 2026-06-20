using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MotionChangeOver : AxisMotionBase
{
    /// <summary>
    /// 品種タグ
    /// </summary>
    [SerializeField]
    protected TagInfo kindTag;

    /// <summary>
    /// 現在の値
    /// </summary>
    [SerializeField]
    private int value;

    /// <summary>
    /// 型替え設定
    /// </summary>
    protected ChangeOverSetting changeOverSetting;

    /// <summary>
    /// 初期位置
    /// </summary>
    private Vector3 initPos;

    /// <summary>
    /// 初期角度
    /// </summary>
    private Vector3 initRot;

    // Start is called before the first frame update
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        if (!isManual)
        {
            value = GetTagValue(changeOverSetting.tag, ref kindTag);
        }
        if (changeOverSetting.isChange)
        {
            var kind = changeOverSetting.datas.Find(d => d.value == value);
            if (kind == null)
            {
                moveObject.transform.localPosition = new Vector3
                {
                    x = initPos.x + changeOverSetting.pos[0] / 1000f,
                    y = initPos.y + changeOverSetting.pos[1] / 1000f,
                    z = initPos.z + changeOverSetting.pos[2] / 1000f,
                };
                moveObject.transform.localEulerAngles = new Vector3
                {
                    x = initRot.x + changeOverSetting.rot[0],
                    y = initRot.y + changeOverSetting.rot[1],
                    z = initRot.z + changeOverSetting.rot[2],
                };
            }
            else
            {
                moveObject.transform.localPosition = new Vector3
                {
                    x = initPos.x + kind.pos[0] / 1000f,
                    y = initPos.y + kind.pos[1] / 1000f,
                    z = initPos.z + kind.pos[2] / 1000f,
                };
                moveObject.transform.localEulerAngles = new Vector3
                {
                    x = initRot.x + kind.rot[0],
                    y = initRot.y + kind.rot[1],
                    z = initRot.z + kind.rot[2],
                };
            }
        }
        else
        {
            moveObject.transform.localPosition = new Vector3
            {
                x = initPos.x + changeOverSetting.pos[0] / 1000f,
                y = initPos.y + changeOverSetting.pos[1] / 1000f,
                z = initPos.z + changeOverSetting.pos[2] / 1000f,
            };
            moveObject.transform.localEulerAngles = new Vector3
            {
                x = initRot.x + changeOverSetting.rot[0],
                y = initRot.y + changeOverSetting.rot[1],
                z = initRot.z + changeOverSetting.rot[2],
            };
            moveObject.SetActive(value != 0);
        }
        /*
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
        */
    }

    /// <summary>
    /// ユニット情報を外部から設定する
    /// </summary>
    /// <param name="unitSetting"></param>
    public void SetUnitSettings(UnitSetting unitSetting, ChuckUnitSetting chuckSetting, ChangeOverSetting changeOverSetting)
    {
        base.SetUnitSettings(unitSetting, chuckSetting);
        this.changeOverSetting = changeOverSetting;
        initPos = moveObject.transform.localPosition;
        initRot = moveObject.transform.localEulerAngles;
    }
}
