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
    /// 接触可能
    /// </summary>
    public bool IsTouch;

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
            collider = this.gameObject.AddComponent<BoxCollider>();
        }
        // 接触可能=物理接触する実体コライダ。OFF=トリガ（すり抜け・検知のみ）
        // ※isTriggerは「接触しない検知用」なので接触可能の否定になる（従来は代入が逆でチェックすると逆にすり抜けていた）
        collider.isTrigger = !IsTouch;
        rigi = GetComponentInChildren<Rigidbody>();
        if (rigi == null)
        {
            rigi = this.gameObject.AddComponent<Rigidbody>();
        }
        rigi.useGravity = IsGravity;
        rigi.sleepThreshold = 0f;
        if (IsGrabbable)
        {
        }
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected override void MyFixedUpdate()
    {
        // ドメインリロード後は非シリアライズ参照が消えるため再取得する
        if (rigi == null)
        {
            rigi = GetComponentInChildren<Rigidbody>();
            if (rigi == null)
            {
                return;
            }
        }
        var distance = Mathf.Sqrt(rigi.transform.position.x * rigi.transform.position.x + rigi.transform.position.y * rigi.transform.position.y + rigi.transform.position.z * rigi.transform.position.z);
        if (distance > AliveDistance)
        {
            // Destroyでなくプールへ返却する（Destroyするとアクティブリストにnullが残り、
            // 長時間運転で全ワーク走査のコストが増え続ける）
            MultiObjectFactoryScript.ReleaseWorkStatic(this.gameObject);
            return;
        }
        // 静止ワークの強制起床。吸盤の接触検出（OnCollisionStay）は接触ペアの両方が寝ると
        // 呼ばれないため、拾う前の静止ワークを起こす目的で入れていた。
        // 吸盤をアタッチ（距離判定）へ移行したため停止している。
        // ※戻す場合はこのコメントを外すこと。吸盤を使う案件では必要になる。
        // ※プール復帰時の起床は MultiObjectFactoryScript の生成処理で個別に行っている
        //   （これを止めた結果、生成位置に居座って次の生成が止まる不具合が出たため）
        //if (rigi.IsSleeping() && !rigi.isKinematic)
        //{
        //    rigi.WakeUp();
        //}
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
