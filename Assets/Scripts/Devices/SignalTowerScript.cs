using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SignalTowerScript : KssBaseScript
{
    /// <summary>
    /// タグ
    /// </summary>
    [SerializeField]
    private TagInfo RedTag;
    [SerializeField]
    private TagInfo YellowTag;
    [SerializeField]
    private TagInfo GreenTag;
    [SerializeField]
    private TagInfo BlueTag;
    [SerializeField]
    private TagInfo WhiteTag;

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

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();

        SetEmmision(RedMaterial, RedTag);
        SetEmmision(YellowMaterial, YellowTag);
        SetEmmision(GreenMaterial, GreenTag);
        SetEmmision(BlueMaterial, BlueTag);
        SetEmmision(WhiteMaterial, WhiteTag);
    }

    private void SetEmmision(Material mat, TagInfo tag)
    {
        if (mat != null)
        {
            if (GlobalScript.GetTagData(tag) == 1)
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

        var st = (SignalTowerSetting)obj;
        if (st.red != null)
        {
            RedTag = ScriptableObject.CreateInstance<TagInfo>();
            RedTag.Database = unitSetting.Database;
            RedTag.MechId = unitSetting.mechId;
            RedTag.Tag = st.red;
        }
        if (st.yellow != null)
        {
            YellowTag = ScriptableObject.CreateInstance<TagInfo>();
            YellowTag.Database = unitSetting.Database;
            YellowTag.MechId = unitSetting.mechId;
            YellowTag.Tag = st.yellow;
        }
        if (st.green != null)
        {
            GreenTag = ScriptableObject.CreateInstance<TagInfo>();
            GreenTag.Database = unitSetting.Database;
            GreenTag.MechId = unitSetting.mechId;
            GreenTag.Tag = st.green;
        }
        if (st.blue != null)
        {
            BlueTag = ScriptableObject.CreateInstance<TagInfo>();
            BlueTag.Database = unitSetting.Database;
            BlueTag.MechId = unitSetting.mechId;
            BlueTag.Tag = st.blue;
        }
        if (st.white != null)
        {
            WhiteTag = ScriptableObject.CreateInstance<TagInfo>();
            WhiteTag.Database = unitSetting.Database;
            WhiteTag.MechId = unitSetting.mechId;
            WhiteTag.Tag = st.white;
        }
    }
}
