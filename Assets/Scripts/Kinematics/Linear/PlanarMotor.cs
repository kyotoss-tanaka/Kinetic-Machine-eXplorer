using Parameters;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

public class PlanarMotor : UseHeadBaseScript
{
    #region プロパティ
    [SerializeField]
    protected GameObject LinearObject;

    [SerializeField]
    protected int OpcUaCh;

    [SerializeField]
    protected TagInfo X;

    [SerializeField]
    protected TagInfo Y;

    [SerializeField]
    protected TagInfo RZ;

    [SerializeField]
    protected int Count = 10;

    [SerializeField]
    protected Vector3 PositionOffset;

    [SerializeField]
    protected Vector3 EulerAnglesOffset;

    [SerializeField]
    protected List<TagInfo> FirstTimeOnIO;

    /// <summary>
    /// 初期オフセット
    /// </summary>
    protected Vector3 InitPosOffset;
    protected Vector3 InitEulerOffset;

    /// <summary>
    /// ベースオブジェクト
    /// </summary>
    protected GameObject objBase;

    /// <summary>
    /// 設定
    /// </summary>
    protected PlanarMotorSetting pm;

    #endregion プロパティ

    /// <summary>
    /// 初回処理判定
    /// </summary>
    private bool IsFirst = true;

    #region 関数
    /// <summary>
    /// 開始時処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.FixedUpdate();

        if (IsFirst)
        {
            foreach (var tag in FirstTimeOnIO)
            {
                GlobalScript.SetTagData(tag, 1);
            }
            IsFirst = false;
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        var ret = base.GetUseTags();
        ret.Add(X);
        ret.Add(Y);
        ret.Add(RZ);
        foreach (var tag in FirstTimeOnIO)
        {
            ret.Add(tag);
        }
        return ret;
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        LinearObject = GetGameObjectFromPrm(components, kssInstanceIds, root, "LinearObject");
        OpcUaCh = GetInt32FromPrm(root, "OpcUaCh");
        X = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "X");
        Y = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "Y");
        RZ = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "RZ");
        Count = GetInt32FromPrm(root, "Count");
        PositionOffset = GetVector3FromPrm(root, "PositionOffset");
        EulerAnglesOffset = GetVector3FromPrm(root, "EulerAnglesOffset");
        FirstTimeOnIO = GetTagInfosFromPrm(components, scriptables, kssInstanceIds, root, "FirstTimeOnIO");
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        LinearObject = unitSetting.unitObject;

        pm = (PlanarMotorSetting)obj;

        Count = pm.count;
        PositionOffset = new Vector3
        {
            x = pm.offset_p[0],
            y = pm.offset_p[2],
            z = pm.offset_p[1]
        };
        EulerAnglesOffset = new Vector3
        {
            x = pm.offset_r[0],
            y = pm.offset_r[2],
            z = pm.offset_r[1]
        };
        if ((pm.tags_p.Count >= 3) && (pm.tags_r.Count >= 3))
        {
            X = ScriptableObject.CreateInstance<TagInfo>();
            X.Database = unitSetting.Database;
            X.MechId = unitSetting.mechId;
            X.Device = pm.tags_p[0];
            Y = ScriptableObject.CreateInstance<TagInfo>();
            Y.Database = unitSetting.Database;
            Y.MechId = unitSetting.mechId;
            Y.Device = pm.tags_p[1];
            RZ = ScriptableObject.CreateInstance<TagInfo>();
            RZ.Database = unitSetting.Database;
            RZ.MechId = unitSetting.mechId;
            RZ.Device = pm.tags_r[2];
        }

        FirstTimeOnIO = new List<TagInfo>();
    }
    #endregion 関数
}
