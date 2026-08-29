using Parameters;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AxisMotionBase;

public class ObjectDeleteScript : KinematicsBase
{
    [SerializeField]
    private TagInfo Tag;

    [SerializeField]
    private float deleteDistance;

    [SerializeField]
    private Vector3 deletePos;

    [SerializeField]
    private int BacketNo = -1;

    /// <summary>
    /// 設定
    /// </summary>
    protected WorkDeleteSetting wkDeleteSetting;

    /// <summary>
    /// 前回のクリアフラグ
    /// </summary>
    private bool isClear = false;

    /// <summary>
    /// バケット情報
    /// </summary>
    private AxisMotionBase.BacketInfo backetInfo;

    /// <summary>
    /// バケットか
    /// </summary>

    private bool isBacket
    {
        get
        {
            return backetInfo != null;
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();

        var clear = GlobalScript.GetTagData(Tag) == 1;
        if (clear && !isClear)
        {
            if (isBacket)
            {
                if ((backetInfo.backetno < 0) || (backetInfo.backetno != BacketNo))
                {
                    return;
                }
            }
            // クリアフラグON
            float dis = Vector3.Distance(transform.localPosition, deletePos);
            if (dis < deleteDistance)
            {
                var dels = GetComponentsInChildren<ObjectScript>();
                foreach (var del in dels)
                {
                    DestroyImmediate(del.gameObject);
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
        BacketNo = wkDeleteSetting.backetno;
        deletePos = new Vector3
        {
            x = wkDeleteSetting.pos[0] * transform.localScale.x,
            y = wkDeleteSetting.pos[1] * transform.localScale.y,
            z = wkDeleteSetting.pos[2] * transform.localScale.z
        };

        // 削除範囲の確認表示（Ctrl+Shift押下中のみ表示）を生成する
        CreateZoneObject();
    }

    /// <summary>削除範囲の確認表示（表示切替はBacketPathOverlayが行う）</summary>
    private GameObject zoneObj;

    /// <summary>
    /// 削除範囲（deletePos中心・半径deleteDistanceの球）を半透明で可視化するオブジェクトを生成する。
    /// 削除判定は親ローカル空間の距離比較のため、球も親の子として同じ空間に置く。
    /// </summary>
    private void CreateZoneObject()
    {
        if (zoneObj != null)
        {
            Destroy(zoneObj);
            zoneObj = null;
        }
        if ((deleteDistance <= 0f) || (transform.parent == null))
        {
            return;
        }
        zoneObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        zoneObj.name = $"WorkDeleteZone_{name}";
        // 判定はあくまで距離比較なのでコライダは不要（ワークとの物理干渉を避けるため必ず除去する）
        var col = zoneObj.GetComponent<Collider>();
        if (col != null)
        {
            Destroy(col);
        }
        zoneObj.transform.SetParent(transform.parent, false);
        zoneObj.transform.localPosition = deletePos;
        zoneObj.transform.localScale = Vector3.one * deleteDistance * 2f;
        var rend = zoneObj.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = SafetyZoneScript.MakeZoneMaterial(new Color(1f, 0.2f, 0.2f, 0.3f));
        }
        zoneObj.SetActive(false);
        BacketPathOverlay.RegisterLine($"{name}_{GetInstanceID()}", zoneObj);
    }

    private void OnDestroy()
    {
        if (zoneObj != null)
        {
            Destroy(zoneObj);
        }
    }

    /// <summary>
    /// バケット情報セット
    /// </summary>
    /// <param name="backetInfo"></param>
    public void SetBacketInfo(AxisMotionBase.BacketInfo backetInfo)
    {
        this.backetInfo = backetInfo;
    }
}
