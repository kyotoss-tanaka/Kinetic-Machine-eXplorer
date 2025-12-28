using Unity.VisualScripting;
using UnityEngine;

public class ToolCollisionScript : MonoBehaviour
{
    public OVRInput.Controller controller;
    public float amplitude = 0.5f;   // êUìÆã≠ìx (0Å`1)
    public float duration = 0.2f;    // êUìÆéûä‘
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
    /// êUìÆäJén
    /// </summary>
    private void TriggerHaptic()
    {
        // êUìÆäJén
        OVRInput.SetControllerVibration(1f, amplitude, controller);

        // duration å„Ç…í‚é~
        Invoke(nameof(StopHaptic), duration);
    }

    /// <summary>
    /// êUìÆí‚é~
    /// </summary>
    private void StopHaptic()
    {
        OVRInput.SetControllerVibration(0, 0, controller);
    }
}
