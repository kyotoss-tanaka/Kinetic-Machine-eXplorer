using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class OVRCustomController : MonoBehaviour
{
    private bool IsSit = false;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            // Triggerを押下
            var camera = this.GetComponentInChildren<OVRCameraRig>();
            if (IsSit)
            {
                camera.transform.localPosition = new Vector3(camera.transform.localPosition.x, 1, camera.transform.localPosition.z);
            }
            else
            {
                camera.transform.localPosition = new Vector3(camera.transform.localPosition.x, 0, camera.transform.localPosition.z);
            }
            IsSit = !IsSit;
        }
        else if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            // Triggerを押下
            var ctrl = this.GetComponentInChildren<OVRPlayerController>();
            ctrl.Jump();
        }
        if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) && OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) && OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            // 精神と時の部屋移動
            var room = FindObjectsByType<Transform>(FindObjectsSortMode.None).ToList().Find(d => d.name == "精神と時の部屋");
            if (room != null)
            {
                room.localPosition = new Vector3(-20, 0, 0);
            }
        }
        else if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) && OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) && OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            var room = FindObjectsByType<Transform>(FindObjectsSortMode.None).ToList().Find(d => d.name == "精神と時の部屋");
            if (room != null)
            {
                room.localPosition = new Vector3(-450, 0, 0);
            }
        }
    }
}
