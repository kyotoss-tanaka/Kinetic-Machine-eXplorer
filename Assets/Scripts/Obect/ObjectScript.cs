using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class ObjectScript : BaseBehaviour
{
    /// <summary>
    /// Rigitbody
    /// </summary>
    private Rigidbody rigi;

    /// <summary>
    /// 生存可能な距離
    /// </summary>
    public float AliveDistance;
    // Start is called before the first frame update

    /// <summary>
    /// 掴める
    /// </summary>
    public bool IsGrabbable;

    /// <summary>
    /// 重力使用
    /// </summary>
    public bool IsGravity;

    /// <summary>
    /// オブジェクトID
    /// </summary>
    public int id;

    /// <summary>
    /// 回転固定
    /// </summary>
    public Vector3 fixedAngles;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        // ワークID取得
        id = GlobalScript.workId;

        var collider = GetComponentInChildren<Collider>();
        if (collider == null)
        {
            this.gameObject.AddComponent<Collider>();
        }
        rigi = GetComponentInChildren<Rigidbody>();
        if (rigi == null)
        {
            rigi = this.gameObject.AddComponent<Rigidbody>();
        }
        rigi.useGravity = IsGravity;
        if (IsGrabbable)
        {
            // 掴める
            var grab = rigi.gameObject.AddComponent<Grabbable>();
            var gft = rigi.gameObject.AddComponent<GrabFreeTransformer>();
            var gi = rigi.gameObject.AddComponent<GrabInteractable>();
            var hgi = rigi.gameObject.AddComponent<HandGrabInteractable>();
            grab.InjectOptionalOneGrabTransformer(gft);
            grab.InjectOptionalTwoGrabTransformer(gft);
            gi.InjectOptionalPointableElement(grab);
            gi.InjectRigidbody(rigi);
            hgi.InjectOptionalPointableElement(grab);
            hgi.InjectRigidbody(rigi);
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        var distance = Mathf.Sqrt(rigi.transform.position.x * rigi.transform.position.x + rigi.transform.position.y * rigi.transform.position.y + rigi.transform.position.z * rigi.transform.position.z);
        if (distance > AliveDistance)
        {
            Destroy(this.gameObject);
        }
//        transform.localEulerAngles = this.fixedAngles;
    }

    /// <summary>
    /// 衝突発生
    /// </summary>
    /// <param name="other"></param>
    protected override void OnCollisionEnter(Collision other)
    {
        base.OnCollisionEnter(other);
    }

    protected override void OnTriggerEnter(Collider other)
    {
        base.OnTriggerEnter(other);
    }
}
