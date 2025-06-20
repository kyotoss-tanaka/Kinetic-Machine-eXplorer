using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class ComMicks : ComBaseScript
{
    /// <summary>
    /// IPアドレス
    /// </summary>
    [SerializeField]
    private string IpAddress = "192.168.3.10";

    /*
    /// <summary>
    /// ポート設定
    /// </summary>
    [SerializeField]
    private int Port = 5900;
    */

    /// <summary>
    /// APIのURL
    /// </summary>
    private string url;

    // Start is called before the first frame update
    protected override void Start()
    {
        url = $"http://{IpAddress}:{Port}/mech?sub=1";
        StartCoroutine(GetData());
    }

    // Update is called once per frame
    protected override void MyFixedUpdate()
    {
        if (!GlobalScript.micksMechs.ContainsKey(IpAddress))
        {
            GlobalScript.micksMechs.Add(IpAddress, new List<GlobalScript.MechInfo>());
        }
    }

    /// <summary>
    /// API通信
    /// </summary>
    /// <returns></returns>
    private IEnumerator GetData()
    {
        while (true)
        {
            UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.isNetworkError || req.isHttpError)
            {
                Debug.Log(req.error);
            }
            else if (req.responseCode == 200)
            {
                // 受信処理
                var ret = GlobalScript.ApiGetValue32(req.downloadHandler.text);
                var tmp = new List<GlobalScript.MechInfo>();
                for (var i = 0; i < ret.Count; i += 4)
                {
                    tmp.Add(new GlobalScript.MechInfo
                    {
                        no = (i / 4) + 1,
                        mechType = (ret[i + 0] >> 16) & 0xFF,
                        pos = new GlobalScript.MechPos
                        {
                            x = ret[i + 1],
                            y = ret[i + 2],
                            z = ret[i + 3]
                        }
                    });
                }
                GlobalScript.micksMechs[IpAddress] = tmp;
            }
            yield return new WaitForSeconds(0.05f);
        }
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="components"></param>
    /// <param name="scriptables"></param>
    /// <param name="kssInstanceIds"></param>
    /// <param name="root"></param>
    public override void SetParameter(List<Component> components, List<KssPartsBase> scriptables, List<KssInstanceIds> kssInstanceIds, JsonElement root)
    {
        base.SetParameter(components, scriptables, kssInstanceIds, root);
        IpAddress = GetStringFromPrm(root, "IpAddress");
        Port = GetInt32FromPrm(root, "Port");
    }
}
