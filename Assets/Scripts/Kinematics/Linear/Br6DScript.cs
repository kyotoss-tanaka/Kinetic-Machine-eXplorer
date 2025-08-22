using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class Br6DScript : PlanarMotor
{
    /// <summary>
    /// シャトル
    /// </summary>
    private List<GameObject> shuttles = new List<GameObject>();

    protected override void Start()
    {
        base.Start();
        // リニアオブジェクトを一旦削除
        if (LinearObject != null)
        {
            // ヘッドをセットする
            if (HeadObject != null)
            {
                HeadObject.transform.parent = LinearObject.transform;
            }
            // 原点に戻す
            InitPosOffset = LinearObject.transform.localPosition;
            InitEulerOffset = LinearObject.transform.localEulerAngles;

            // 再生成用
            objBase = new GameObject("Br6DFuctory");
            objBase.transform.position = LinearObject.transform.position;
            objBase.transform.eulerAngles = LinearObject.transform.eulerAngles;

            // 一度削除する
            Destroy(LinearObject);

            for (var i = 0; i < Count; i++)
            {
                var sh = Instantiate(LinearObject);
                sh.transform.parent = LinearObject.transform;
                sh.transform.localPosition = new Vector3();
                sh.transform.eulerAngles = new Vector3();
                sh.transform.parent = objBase.transform;
                var del = sh.GetComponent<ObjectDeleteScript>();
                if (del != null)
                {
                    Destroy(del);
                    foreach (var wk in unitSetting.workDeleteSettings)
                    {
                        var s = sh.transform.AddComponent<ObjectDeleteScript>();
                        s.SetParameter(unitSetting, wk);
                    }
                }
                shuttles.Add(sh);
            }
        }
    }

    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if (objBase != null)
        {
            /*
            var opcua = GlobalScript.opcuas.FirstOrDefault(d => (d.Key != "") && (d.Value.Ch == OpcUaCh));
            objBase.transform.localPosition = InitPosOffset + PositionOffset;
            objBase.transform.localEulerAngles = InitEulerOffset + EulerAnglesOffset;
            for (var i = 0; i < Count; i++)
            {
                var posX = GlobalScript.GetTagFData(opcua.Key, "OpcUA", X + "[" + i + "]");
                var posY = GlobalScript.GetTagFData(opcua.Key, "OpcUA", Y + "[" + i + "]");
                var rotZ = GlobalScript.GetTagFData(opcua.Key, "OpcUA", RZ + "[" + i + "]");
                shuttles[i].transform.localPosition = new Vector3(posX, 0, posY);
                shuttles[i].transform.localEulerAngles = new Vector3(0, rotZ, 0);
            }
            */
            objBase.transform.localPosition = InitPosOffset + PositionOffset;
            objBase.transform.localEulerAngles = InitEulerOffset + EulerAnglesOffset;
            for (var i = 0; i < shuttles.Count; i++)
            {
                var posX = GlobalScript.GetTagData(X.Database, X.MechId, X.Device + "[" + i + "]") / 1000000f * pm.dir_p[0];
                var posY = GlobalScript.GetTagData(Y.Database, Y.MechId, Y.Device + "[" + i + "]") / 1000000f * pm.dir_p[1];
                var rotZ = GlobalScript.GetTagData(RZ.Database, RZ.MechId, RZ.Device + "[" + i + "]") / 1000000f * pm.dir_r[2];
                shuttles[i].transform.localPosition = new Vector3(posX, 0, posY);
                shuttles[i].transform.localEulerAngles = new Vector3(0, rotZ, 0);
            }

        }
    }

    public GameObject GetNearShuttle(Vector3 vector)
    {
        GameObject ret = null;
        float distance = 1000;
        foreach (var sh in shuttles)
        {
            float dis = Vector3.Distance(sh.transform.position, vector);
            if (distance > dis)
            {
                ret = sh;
                distance = dis;
            }
        }
        return ret;
    }

    protected override void OnCollisionEnter(Collision other)
    {
        base.OnCollisionEnter(other);
    }

    protected override void OnDestroy()
    {
        Destroy(objBase);
        base.OnDestroy();
    }
}
