using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseBehaviour : MonoBehaviour
{
    protected virtual void Reset() { }
    protected virtual void Awake() { }
    protected virtual void OnEnable() { }
    protected virtual void Start() { }
    protected virtual void OnDisable() { }
    protected virtual void OnDestroy() { }
    protected virtual void OnTriggerEnter(Collider other) { }
    protected virtual void OnTriggerEnter2D(Collider2D other) { }
    protected virtual void OnTriggerStay(Collider other) { }
    protected virtual void OnTriggerStay2D(Collider2D other) { }
    protected virtual void OnTriggerExit(Collider other) { }
    protected virtual void OnTriggerExit2D(Collider2D other) { }
    protected virtual void OnCollisionEnter(Collision other) { }
    protected virtual void OnCollisionEnter2D(Collision2D other) { }
    protected virtual void OnCollisionStay(Collision other) { }
    protected virtual void OnCollisionStay2D(Collision2D other) { }
    protected virtual void OnCollisionExit(Collision other) { }
    protected virtual void OnCollisionExit2D(Collision2D other) { }
    public virtual void OnMouseEnter() { }
    public virtual void OnMouseOver() { }
    public virtual void OnMouseUp() { }
    protected virtual void OnMouseDrag() { }
    public virtual void OnMouseDown() { }
    protected virtual void OnMouseUpAsButton() { }
    public virtual void OnMouseExit() { }
    protected virtual void LateUpdate() { }
    /*
    protected virtual void OnWillRenderObject() { }
    protected virtual void OnPreCull() { }
    protected virtual void OnBecameVisible() { }
    protected virtual void OnBecameInvisible() { }
    protected virtual void OnPreRender() { }
    protected virtual void OnRenderObject() { }
    protected virtual void OnPostRender() { }
    protected virtual void OnRenderImage(RenderTexture src, RenderTexture dest) { }
    protected virtual void OnDrawGizmos() { }
    protected virtual void OnGUI() { }
    protected virtual void OnApplicationPause(bool pauseStatus) { }
    protected virtual void OnApplicationQuit() { }
    protected virtual void OnApplicationFocus(bool focusStatus) { }
    */
    private Coroutine updateProceess { get; set; }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected virtual void Update()
    {
    }

    /// <summary>
    /// 更新処理
    /// </summary>
    protected virtual void FixedUpdate()
    {
        if (updateProceess == null)
        {
            updateProceess = StartCoroutine(UpdateProcess());
        }
    }

    IEnumerator UpdateProcess()
    {
        // 全スクリプトの MyFixedUpdate がこのコルーチン1本に合算されて計測されるため、
        // 実体クラス名のマーカーを付けてプロファイラで内訳が見えるようにする
        marker = new Unity.Profiling.ProfilerMarker(GetType().Name + ".MyFixedUpdate");
        // WaitForFixedUpdate は状態を持たないので使い回す。
        // ループ内で new すると「全インスタンス×毎FixedUpdate」でゴミが出続ける
        var wait = new WaitForFixedUpdate();
        while (true)
        {
            yield return wait;
            InvokeMyFixedUpdate();
        }
    }

    private Unity.Profiling.ProfilerMarker marker;

    /// <summary>
    /// MyFixedUpdate で例外が出た回数
    /// </summary>
    private int errorCount = 0;

    /// <summary>
    /// 同じ例外を出し続けたときにログへ出す間隔(回)。50Hz で約6秒ごと
    /// </summary>
    private const int ErrorLogInterval = 300;

    /// <summary>
    /// 計測マーカー付きで MyFixedUpdate を呼ぶ（using をイテレータ外に置くため別メソッドにする）
    /// </summary>
    private void InvokeMyFixedUpdate()
    {
        using (marker.Auto())
        {
            try
            {
                MyFixedUpdate();
            }
            catch (Exception ex)
            {
                // コルーチンの中で例外を外へ投げると、Unity はそのコルーチンを停止する。
                // つまり一度の例外でこのスクリプトの更新が二度と呼ばれなくなり、
                // エラーが1行出たあとは静かに何もしなくなる
                // （実際に MultiObjectFactoryScript が止まってワーク生成が停止した）。
                // 通常の Update/FixedUpdate は Unity 側が呼び出しごとに例外を受け止めるので
                // こうはならない。コルーチン経路だけの問題なのでここで受ける。
                //
                // 握りつぶすと原因が分からなくなるため、内容とスタックは必ずログへ出す。
                // 毎フレーム出続けるとログが埋まるので、最初の1回と一定回数ごとに絞る。
                errorCount++;
                if ((errorCount == 1) || ((errorCount % ErrorLogInterval) == 0))
                {
                    Debug.LogError($"[{GetType().Name}] {name} の MyFixedUpdate で例外が発生しました"
                        + $"（{errorCount}回目・処理は継続します）: {ex}");
                }
            }
        }
    }

    /// <summary>
    /// 更新処理コルーチン
    /// </summary>
    protected virtual void MyFixedUpdate()
    {
    }
}