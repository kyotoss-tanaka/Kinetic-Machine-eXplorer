using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseBehaviour : MonoBehaviour
{
    protected virtual void Reset() { }
    protected virtual void Awake() { }
    protected virtual void OnEnable() { }
    protected virtual void Start() { }
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
    protected virtual void OnDisable() { }
    protected virtual void OnDestroy() { }
    protected virtual void OnApplicationQuit() { }
    protected virtual void OnApplicationFocus(bool focusStatus) { }
    
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
        while (true)
        {
            yield return new WaitForFixedUpdate();
            MyFixedUpdate();
        }
    }

    /// <summary>
    /// 更新処理コルーチン
    /// </summary>
    protected virtual void MyFixedUpdate()
    {
    }
}