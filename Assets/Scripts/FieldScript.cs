using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FieldScript : BaseBehaviour
{
    protected override void OnCollisionEnter(Collision collision)
    {
        if (this.enabled)
        {
            base.OnCollisionEnter(collision);
            var obj = collision.transform.GetComponentInChildren<ObjectScript>();
            if (obj == null)
            {
                obj = collision.transform.GetComponentInParent<ObjectScript>();
            }
            if (obj != null)
            {
                Destroy(obj.gameObject);
            }
        }
    }
}
