using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;

public class Kinematics3D : KinematicsBase
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    #region プロパティ
    [SerializeField]
    protected TagInfo X;

    [SerializeField]
    protected TagInfo Y;

    [SerializeField]
    protected TagInfo Z;

    [SerializeField]
    protected Vector3 target;

    protected List<MotionInternal> tmActs { get; set; } = new();

    protected float offsetX;
    protected float offsetY;
    protected float offsetZ;

    /// <summary>
    /// 初期の目標（ツールオフセットを足す前）。型がモデルの初期姿勢の先端位置を求めて入れる（求めない型は 0）
    /// </summary>
    protected Vector3 initialTargetRaw = Vector3.zero;

    /// <summary>
    /// 初期の目標を型が求めたか（求めない型は従来どおり 0 を使う）
    /// </summary>
    protected bool hasInitialTarget = false;

    /// <summary>
    /// 初期の目標（ツールオフセット込み。タグが空の軸と、手動に切り替えた時の始めの値に使う）
    /// </summary>
    protected Vector3 InitialTarget
    {
        get
        {
            return hasInitialTarget ? initialTargetRaw + new Vector3(offsetX, offsetY, offsetZ) : Vector3.zero;
        }
    }
    #endregion プロパティ

    #region 変数
    protected RobotSetting robo;
    protected float txMax = 0;
    protected float txMin = 0;
    protected float tyMax = 0;
    protected float tyMin = 0;
    protected float tzMax = 0;
    protected float tzMin = 0;
    #endregion 変数

    #region 関数

    // Start is called before the first frame update
    protected override void Start()
    {
        if (baseObject == null)
        {
            ModelRestruct();
        }
        // 初期の目標をモデルの初期姿勢にし、Inspector の目標にも入れておく（手動に切り替えた時にここから動き始める）
        InitModelPoseTarget();
        target = InitialTarget;
    }

    /// <summary>
    /// 型がモデルの初期姿勢から目標（SetTarget に渡す形＝ツールオフセットを引いた値）を求める。求められない型は false
    /// </summary>
    protected virtual bool TryModelPoseTarget(out Vector3 raw)
    {
        raw = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 初期の目標をモデルの初期姿勢にする。求めた目標で一度 SetTarget を呼び、
    /// ロボットの全部品の向き・位置がモデルと一致することを確かめてから使う（一致しなければ部品を元に戻し、従来どおり 0）
    /// </summary>
    protected void InitModelPoseTarget()
    {
        if (hasInitialTarget || !TryModelPoseTarget(out var raw))
        {
            return;
        }
        if (VerifyModelPose(() => SetTarget(raw.x, raw.y, raw.z), out var error))
        {
            initialTargetRaw = raw;
            hasInitialTarget = true;
            Debug.Log($"[Robot] {unitSetting.name}: 初期の目標＝モデルの初期姿勢 ({raw.x:0.0}, {raw.y:0.0}, {raw.z:0.0})（オフセット前）");
        }
        else
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: モデルの初期姿勢を目標にできませんでした（{error}）。初期の目標は 0 のままにします");
        }
    }

    /// <summary>
    /// apply を実行して、ロボットの全部品の向き・位置がモデルの初期姿勢と一致するかを確かめる。
    /// 一致しなければ部品を元に戻す
    /// </summary>
    protected bool VerifyModelPose(System.Action apply, out string error)
    {
        error = "";
        var root = (unitSetting != null) ? unitSetting.moveObject : null;
        if (root == null)
        {
            error = "モデルが無い";
            return false;
        }
        var parts = root.GetComponentsInChildren<Transform>(true);
        var rots = new Quaternion[parts.Length];
        var poss = new Vector3[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            rots[i] = parts[i].localRotation;
            poss[i] = parts[i].localPosition;
        }
        try
        {
            apply();
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
        }
        var maxAngle = 0f;
        var maxMove = 0f;
        var worst = "";
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null)
            {
                continue;
            }
            var a = Quaternion.Angle(rots[i], parts[i].localRotation);
            var m = Vector3.Distance(poss[i], parts[i].localPosition) * 1000f;
            if ((a > maxAngle) || (m > maxMove))
            {
                worst = parts[i].name;
            }
            maxAngle = Mathf.Max(maxAngle, float.IsNaN(a) ? 999f : a);
            maxMove = Mathf.Max(maxMove, float.IsNaN(m) ? 999f : m);
        }
        var ok = (error == "") && (maxAngle < 0.05f) && (maxMove < 0.05f);
        if (!ok)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null)
                {
                    parts[i].localRotation = rots[i];
                    parts[i].localPosition = poss[i];
                }
            }
            if (error == "")
            {
                error = $"部品のずれ 最大 {maxAngle:0.000}° / {maxMove:0.000}mm（{worst}）";
            }
        }
        return ok;
    }

    /// <summary>
    /// マニュアルで Z（ヘッドの回転）を動かせるか（ヘッドを回す軸が無い型は false）
    /// </summary>
    protected virtual bool HasManualZ
    {
        get { return true; }
    }

    /// <summary>
    /// マニュアルで動かせる軸（目標の X・Y（mm）と Z（°））
    /// </summary>
    public override List<ManualAxis> GetManualAxes()
    {
        var axes = new List<ManualAxis>
        {
            new ManualAxis { name = "X", unit = "mm", get = () => target.x, set = v => target.x = v, dragStep = 1f },
            new ManualAxis { name = "Y", unit = "mm", get = () => target.y, set = v => target.y = v, dragStep = 1f },
        };
        if (HasManualZ)
        {
            axes.Add(new ManualAxis { name = "Z", unit = "°", get = () => target.z, set = v => target.z = v, dragStep = 0.5f });
        }
        return axes;
    }

    protected override void MyFixedUpdate()
    {
        if (isManual)
        {
            setTarget(target);
        }
        else
        {
            if (robo.isTm)
            {
                // タイムチャートからタグ取得
                if (tmActs.Count > 0)
                {
                    var x = tmActs[0] != null ? tmActs[0].nowValue : 0;
                    var y = tmActs[1] != null ? tmActs[1].nowValue : 0;
                    var z = tmActs[2] != null ? tmActs[2].nowValue : 0;
                    // mm単位系に変換
                    target.x = x * 1000f;
                    target.y = y * 1000f;
                    target.z = z * 1000f;
                    setTarget(target);
                }
            }
            else
            {
                // タグからデータ取得
                if (robo.tags.Count >= 3)
                {
                    // タグが空の軸は 0 ではなく初期の目標（モデルの初期姿勢）を使う
                    // （以前は 0 になり、範囲を持たない型ではアームを根元に折りたたんだ姿勢になっていた）
                    var initial = InitialTarget;
                    var x = string.IsNullOrEmpty(robo.tags[0]) ? initial.x : GetTagValueF(robo.tags[0], ref X) / (robo.rates[0] == 0 ? 1000f : robo.rates[0] / 1000f);
                    var y = string.IsNullOrEmpty(robo.tags[1]) ? initial.y : GetTagValueF(robo.tags[1], ref Y) / (robo.rates[1] == 0 ? 1000f : robo.rates[1] / 1000f);
                    var z = string.IsNullOrEmpty(robo.tags[2]) ? initial.z : GetTagValueF(robo.tags[2], ref Z) / (robo.rates[2] == 0 ? 1000f : robo.rates[2] / 1000f);
                    // mm単位系に変換済み
                    target.x = CheckRangeF(x, txMin, txMax);
                    target.y = CheckRangeF(y, tyMin, tyMax);
                    target.z = CheckRangeF(z, tzMin, tzMax);
                    setTarget(target);
                }
            }
        }
    }

    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public override List<TagInfo> GetUseTags()
    {
        return new List<TagInfo> { X, Y, Z };
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="target"></param>
    public virtual void setTarget(Vector3 target)
    {
        SetTarget(target.x - offsetX, target.y - offsetY, target.z - offsetZ);
    }

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public virtual void SetTarget(float x, float y, float z)
    {
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected override void SetCollision()
    {
        /*
        // 当たり判定追加
        foreach (var mesh in this.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = mesh.GetComponentsInChildren<MeshFilter>();
            int polygonCount = 0;
            foreach (var m in mf)
            {
                polygonCount += m.sharedMesh.triangles.Length / 3;
            }
            if (polygonCount < 256)
            {
                var col = mesh.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
            else
            {
                var col = mesh.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
        }
        */
        /*
        // 当たり判定追加
        foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.GetComponentInChildren<Collider>() == null)
            {
                var col = mesh.AddComponent<MeshCollider>();
                col.convex = true;
                col.isTrigger = true;
            }
        }
        var rigi = this.AddComponent<Rigidbody>();
        if (rigi != null)
        {
            rigi.useGravity = false;
            rigi.isKinematic = true;
            if (IsCollision)
            {
                this.AddComponent<CollisionScript>();
            }
        }
        */
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
        X = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "X");
        Y = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "Y");
        Z = GetTagInfoFromPrm(scriptables, kssInstanceIds, root, "Z");
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        robo = (RobotSetting)obj;
        base.SetParameter(unitSetting, robo);
        tmActs = new();
        foreach (var tm in robo.tmUnits)
        {
            if ((tm != null) && (tm.unitObject != null))
            {
                tmActs.Add(tm.unitObject.GetComponent<MotionInternal>());
            }
            else
            {
                tmActs.Add(null);
            }
        }
        // ツールオフセット
        offsetX = 0;
        offsetY = 0;
        offsetZ = 0;
        if (robo.offset != null)
        {
            offsetX = robo.offset.Count < 1 ? 0:  robo.offset[0];
            offsetY = robo.offset.Count < 2 ? 0 : robo.offset[1];
            offsetZ = robo.offset.Count < 3 ? 0 : robo.offset[2];
        }
    }
    #endregion 関数
}
