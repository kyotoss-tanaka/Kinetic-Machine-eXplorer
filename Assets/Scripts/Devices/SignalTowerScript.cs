using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SignalTowerScript : KssBaseScript
{
    [SerializeField]
    private int red;
    [SerializeField]
    private int yellow;
    [SerializeField]
    private int green;
    [SerializeField]
    private int blue;
    [SerializeField]
    private int white;

    private TagInfo RedTag;
    private TagInfo YellowTag;
    private TagInfo GreenTag;
    private TagInfo BlueTag;
    private TagInfo WhiteTag;

    private SignalTowerSetting st;
    private Material RedMaterial;
    private Material YellowMaterial;
    private Material GreenMaterial;
    private Material BlueMaterial;
    private Material WhiteMaterial;

    // Start is called before the first frame update
    protected override void Start()
    {
        var red = transform.GetComponentsInChildren<Transform>().Where(d => d.name == "Red").ToList().FirstOrDefault();
        var yellow = transform.GetComponentsInChildren<Transform>().Where(d => d.name == "Yellow").ToList().FirstOrDefault();
        var green = transform.GetComponentsInChildren<Transform>().Where(d => d.name == "Green").ToList().FirstOrDefault();
        var blue = transform.GetComponentsInChildren<Transform>().Where(d => d.name == "Blue").ToList().FirstOrDefault();
        var white = transform.GetComponentsInChildren<Transform>().Where(d => d.name == "White").ToList().FirstOrDefault();
        RedMaterial = red == null ? null : red.GetComponent<MeshRenderer>().material;
        YellowMaterial = yellow == null ? null : yellow.GetComponent<MeshRenderer>().material;
        GreenMaterial = green == null ? null : green.GetComponent<MeshRenderer>().material;
        BlueMaterial = blue == null ? null : blue.GetComponent<MeshRenderer>().material;
        WhiteMaterial = white == null ? null : white.GetComponent<MeshRenderer>().material;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();

        if (!isManual)
        {
            red = GetTagValue(st.red, ref RedTag);
            yellow = GetTagValue(st.yellow, ref YellowTag);
            green = GetTagValue(st.green, ref GreenTag);
            blue = GetTagValue(st.blue, ref BlueTag);
            white = GetTagValue(st.white, ref WhiteTag);
        }

        SetEmmision(RedMaterial, st.red, red);
        SetEmmision(YellowMaterial, st.yellow, yellow);
        SetEmmision(GreenMaterial, st.green, green);
        SetEmmision(BlueMaterial, st.blue, blue);
        SetEmmision(WhiteMaterial, st.white, white);
    }

    private void SetEmmision(Material mat, string name, int value)
    {
        if (name != "")
        {
            if (value == 1)
            {
                mat.EnableKeyword("_EMISSION");

            }
            else
            {
                mat.DisableKeyword("_EMISSION");

            }
        }
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        st = (SignalTowerSetting)obj;
        RedTag = null;
        YellowTag = null;
        GreenTag = null;
        BlueTag = null;
        WhiteTag = null;
    }
}
