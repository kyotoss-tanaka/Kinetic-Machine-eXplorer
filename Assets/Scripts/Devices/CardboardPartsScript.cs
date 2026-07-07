using Parameters;
using System;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// 段ボール用スクリプト
/// </summary>
public class CardboardPartsScript: KssBaseScript
{
    /// <summary>
    /// フラップ部分
    /// </summary>
    public bool isFlap = false;

    /// <summary>
    /// メッシュコライダー
    /// </summary>
    public MeshCollider meshCollider;

    /// <summary>
    /// ボックスコライダー
    /// </summary>
    public BoxCollider boxCollider;

    /// <summary>
    /// 以前の親
    /// </summary>
    private GameObject prvParent;

    /// <summary>
    /// 初期位置
    /// </summary>
    private Vector3 initPosition;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        // 初期化処理
        Initialize();
    }

    /// <summary>
    /// 周期処理
    /// </summary>
    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (prvParent != transform.parent.gameObject)
        {
            initPosition = transform.localPosition;
            prvParent = transform.parent.gameObject;
        }
        // 位置は変えず角度だけを変える
        this.transform.localPosition = initPosition;
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        // 衝突検知用
        var mr = GetComponentInChildren<MeshRenderer>();
        if (mr != null)
        {
            boxCollider = mr.gameObject.GetComponent<BoxCollider>();
            /*
            if (transform.localScale.x > 0)
            {
                if (boxCollider == null)
                {
                    boxCollider = mr.gameObject.AddComponent<BoxCollider>();
                }
            }
            else
            {
                if (boxCollider != null)
                {
                    Destroy(boxCollider);
                }
                meshCollider = mr.gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
            }
            */
            // 線/点トポロジのメッシュは MeshCollider(convex) を焼けない（PhysXがエラー多発・abort誘因）。
            // ただし何も付けないと段ボールに衝突形状が無く、面に乗らず突き抜ける（cf4f4a1 以降の回帰）。
            // → cook 可能なら convex MeshCollider、不可なら cook 不要の BoxCollider で必ず実体コライダーを確保する。
            var cbMesh = mr.GetComponent<MeshFilter>();
            if (cbMesh != null && MeshColliderUtil.IsCookable(cbMesh.sharedMesh))
            {
                if (boxCollider != null) { Destroy(boxCollider); boxCollider = null; }
                meshCollider = mr.gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;
            }
            else if (boxCollider == null)
            {
                boxCollider = mr.gameObject.AddComponent<BoxCollider>();
            }
        }
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
    }

    /// <summary>
    /// 衝突時処理
    /// </summary>
    /// <param name="collision"></param>
    protected override void OnCollisionEnter(Collision collision)
    {
    }

    protected override void OnCollisionStay(Collision collision)
    {
    }
}
