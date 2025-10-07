using Parameters;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using NCalc;
using static Br6DScript;
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
    protected TagInfo Z;

    [SerializeField]
    protected TagInfo RX;

    [SerializeField]
    protected TagInfo RY;

    [SerializeField]
    protected TagInfo RZ;

    [SerializeField]
    protected int Count = 10;

    [SerializeField]
    protected Vector3 PositionOffset;

    [SerializeField]
    protected Vector3 EulerAnglesOffset;

    /// <summary>
    /// ベースオブジェクト
    /// </summary>
    protected GameObject objBase;

    /// <summary>
    /// 設定
    /// </summary>
    protected PlanarMotorSetting pm;

    /// <summary>
    /// シャトルID
    /// </summary>
    protected List<int> ids = new();

    #endregion プロパティ

    /// <summary>
    /// 初回処理判定
    /// </summary>
    private bool IsFirst = true;

    /// <summary>
    /// シャトル
    /// </summary>
    protected List<GameObject> shuttles = new List<GameObject>();

    /// <summary>
    /// シャトルタグ情報
    /// </summary>
    protected List<ShuttleTagInfo> shuttleTags = new List<ShuttleTagInfo>();

    #region 関数
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// 開始時処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        base.FixedUpdate();

        if (objBase != null)
        {
            objBase.transform.localPosition = PositionOffset;
            objBase.transform.localEulerAngles = EulerAnglesOffset;
            for (var i = 0; i < Count; i++)
            {
                if (shuttleTags.Count != shuttles.Count)
                {
                    break;
                }
                var x = GetTagValueF(pm.tags_p[0], ref shuttleTags[i].X, ids[i]);
                var y = GetTagValueF(pm.tags_p[1], ref shuttleTags[i].Y, ids[i]);
                var z = GetTagValueF(pm.tags_p[2], ref shuttleTags[i].Z, ids[i]);
                var rx = GetTagValueF(pm.tags_r[0], ref shuttleTags[i].RX, ids[i]);
                var ry = GetTagValueF(pm.tags_r[1], ref shuttleTags[i].RY, ids[i]);
                var rz = GetTagValueF(pm.tags_r[2], ref shuttleTags[i].RZ, ids[i]);
                SetTarget(i,
                    x / pm.rate_p[0] * pm.dir_p[0], y / pm.rate_p[1] * pm.dir_p[1], z / pm.rate_p[2] * pm.dir_p[2],
                    rx / pm.rate_r[0] * pm.dir_r[0], ry / pm.rate_r[1] * pm.dir_r[1], rz / pm.rate_r[2] * pm.dir_r[2]
                );
            }
        }
    }

    /// <summary>
    /// 座標セット
    /// </summary>
    /// <param name="index"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="rx"></param>
    /// <param name="ry"></param>
    /// <param name="rz"></param>
    public virtual void SetTarget(int index, float x, float y, float z, float rx, float ry, float rz)
    {
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
        ret.Add(Z);
        ret.Add(RX);
        ret.Add(RY);
        ret.Add(RZ);
        return ret;
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        pm = (PlanarMotorSetting)obj;

        LinearObject = pm.moverUnit == null ? null : pm.moverUnit.unitObject;

        PositionOffset = new Vector3
        {
            x = pm.offset_p[0],
            y = pm.offset_p[1],
            z = pm.offset_p[2]
        };
        EulerAnglesOffset = new Vector3
        {
            x = pm.offset_r[0],
            y = pm.offset_r[1],
            z = pm.offset_r[2]
        };

        // シャトル数
        Count = pm.count;

        bool error = false;
        ids = new List<int>();
        try
        {
            Expression e = new Expression(pm.calc);
            for (var i = 0; i < Count; i++)
            {
                string formula = pm.calc;
                e.Parameters["i"] = i;
                var result = e.Evaluate();
                if (result == null)
                {
                    error = true;
                    break;
                }
                else
                {
                    if (result is int id)
                    {
                        if (ids.Count >= Count)
                        {
                            break;
                        }
                        ids.Add(id);
                    }
                }
            }
        }
        catch
        {
            error = true;
        }
        if (error)
        {
            ids = new List<int>();
            for (var i = 0; i < Count; i++)
            {
                ids.Add(i);
            }
        }

        // リニアオブジェクトを一旦削除
        if (LinearObject != null)
        {
            // 一度削除する
            LinearObject.SetActive(false);

            // 再生成用
            if (objBase == null)
            {
                objBase = new GameObject("MoverFuctory");
                objBase.transform.parent = unitSetting.unitObject.transform;
                objBase.transform.localPosition = new();
                objBase.transform.localEulerAngles = new();
            }
            foreach (var sh in shuttles)
            {
                Destroy(sh);
            }
            shuttles = new List<GameObject>();
            shuttleTags = new List<ShuttleTagInfo>();
            for (var i = 0; i < ids.Count; i++)
            {
                var sh = Instantiate(LinearObject);
                sh.SetActive(true);
                sh.transform.parent = objBase.transform;
                sh.transform.localPosition = new Vector3();
                sh.transform.eulerAngles = new Vector3();
                var del = sh.GetComponent<ObjectDeleteScript>();
                if (del != null)
                {
                    Destroy(del);
                    foreach (var wk in unitSetting.workDeleteSettings)
                    {
                        var s = sh.transform.AddComponent<ObjectDeleteScript>();
                        s.SetParameter(unitSetting, wk);
                    }
                }
                shuttles.Add(sh);
                shuttleTags.Add(new ShuttleTagInfo());
            }
        }
    }
    #endregion 関数
}
