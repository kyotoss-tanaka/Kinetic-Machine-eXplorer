using UnityEngine;

public class ToolCollisionScript : MonoBehaviour
{
    public OVRInput.Controller controller;
    public float amplitude = 0.5f;   // U“®‹­“x (0`1)
    public float duration = 0.1f;    // U“®ŠÔ

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnTriggerEnter(Collider other)
    {
        TriggerHaptic();
    }

    /// <summary>
    /// U“®ŠJn
    /// </summary>
    private void TriggerHaptic()
    {
        // U“®ŠJn
        OVRInput.SetControllerVibration(1f, amplitude, controller);

        // duration Œã‚É’â~
        Invoke(nameof(StopHaptic), duration);
    }

    /// <summary>
    /// U“®’â~
    /// </summary>
    private void StopHaptic()
    {
        OVRInput.SetControllerVibration(0, 0, controller);
    }
}
