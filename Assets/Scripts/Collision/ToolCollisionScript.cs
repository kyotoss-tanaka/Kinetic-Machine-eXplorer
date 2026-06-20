using Unity.VisualScripting;
using UnityEngine;

public class ToolCollisionScript : MonoBehaviour
{
    public OVRInput.Controller controller;
    public float amplitude = 0.5f;   // 振動強度 (0～1)
    public float duration = 0.2f;    // 振動時間
    public Collider colliderObject;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var rb = transform.GetComponent<Rigidbody>();
        if(rb == null)
        {
            rb = transform.AddComponent<Rigidbody>();
        }
        rb.useGravity = false;
        rb.isKinematic = true;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.name.Contains("Controller"))
        {
            TriggerHaptic();
        }
        else
        {
            colliderObject = other;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.name.Contains("Controller"))
        {
            TriggerHaptic();
        }
        else
        {
            colliderObject = null;
        }
    }

    /// <summary>
    /// 振動開始
    /// </summary>
    private void TriggerHaptic()
    {
        // 振動開始
        OVRInput.SetControllerVibration(1f, amplitude, controller);

        // duration 後に停止
        Invoke(nameof(StopHaptic), duration);
    }

    /// <summary>
    /// 振動停止
    /// </summary>
    private void StopHaptic()
    {
        OVRInput.SetControllerVibration(0, 0, controller);
    }
}
