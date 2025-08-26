using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public class Robo6Axis : UseHeadBase3DScript
{
    #region プロパティ
    [SerializeField]
    protected TagInfo J1;

    [SerializeField]
    protected TagInfo J2;

    [SerializeField]
    protected TagInfo J3;

    [SerializeField]
    protected TagInfo J4;

    [SerializeField]
    protected TagInfo J5;

    [SerializeField]
    protected TagInfo J6;
    #endregion プロパティ

    #region 変数
    protected GameObject j1Object;
    protected GameObject j2Object;
    protected GameObject j3Object;
    protected GameObject j4Object;
    protected GameObject j5Object;
    protected GameObject j6Object;

    protected TestPosition testJ1 = new TestPosition { min = -130, max = 130, step = 2, range = 1, target = 0};
    protected TestPosition testJ2 = new TestPosition { min = -90, max = 90, step = 3, range = 1, target = 0};
    protected TestPosition testJ3 = new TestPosition { min = -100, max = 100, step = 2, range = 1, target = 0};
    protected TestPosition testJ4 = new TestPosition { min = -90, max = 90, step = 3, range = 1, target = 0};
    protected TestPosition testJ5 = new TestPosition { min = -120, max = 120, step = 2, range = 1, target = 0};
    protected TestPosition testJ6 = new TestPosition { min = -180, max = 180, step = 3, range = 1, target = 0};

    protected float tj1 = 0, tj2 = 0, tj3 = 0, tj4 = 0, tj5 = 0, tj6 = 0;
    #endregion 変数

    #region 関数
    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        if ((J1 == null) || (J2 == null) || (J3 == null) || (J4 == null) || (J5 == null) || (J6 == null))
        {
            tj1 = RenewNextPosition(testJ1);
            tj2 = RenewNextPosition(testJ2);
            tj3 = RenewNextPosition(testJ3);
            tj4 = RenewNextPosition(testJ4);
            tj5 = RenewNextPosition(testJ5);
            tj6 = RenewNextPosition(testJ6);
        }
        else
        {
            tj1 = GlobalScript.GetTagData(J1);
            tj2 = GlobalScript.GetTagData(J2);
            tj3 = GlobalScript.GetTagData(J3);
            tj4 = GlobalScript.GetTagData(J4);
            tj5 = GlobalScript.GetTagData(J5);
            tj6 = GlobalScript.GetTagData(J6);
        }
        setTarget(tj1, tj2, tj3, tj4, tj5, tj6);
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public virtual void setTarget(float j1, float j2, float j3, float j4, float j5, float j6)
    {
        j1Object.transform.localEulerAngles = new Vector3(0, j1, 0);
        j2Object.transform.localEulerAngles = new Vector3(j2, 0, 0);
        j3Object.transform.localEulerAngles = new Vector3(j3, 0, 0);
        j4Object.transform.localEulerAngles = new Vector3(j4, 0, 0);
        j5Object.transform.localEulerAngles = new Vector3(j5, 0, 0);
        j6Object.transform.localEulerAngles = new Vector3(j6, 0, 0);
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        var ret = base.GetUseTags();
        ret.Add(J1);
        ret.Add(J2);
        ret.Add(J3);
        ret.Add(J4);
        ret.Add(J5);
        ret.Add(J6);
        return ret;
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        J1 = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "J1");
        J2 = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "J2");
        J3 = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "J3");
        J4 = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "J4");
        J5 = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "J5");
        J6 = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "J6");
    }
    #endregion 関数
}
