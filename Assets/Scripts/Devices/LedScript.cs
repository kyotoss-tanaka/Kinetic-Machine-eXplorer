using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SwitchScript;

public class LedScript : KssBaseScript
{
    /// <summary>
    /// LEDカラー
    /// </summary>
    public enum LedColor
    {
        Red, Green, Yellow, Blue, White
    }

    /// <summary>
    /// LEDデータ
    /// </summary>
    public class LedData
    {
        public TagInfo tag;
        public string color;
        public Material material;
    }

    /// <summary>
    /// 動作タイプ
    /// </summary>
    [SerializeField]
    private int type { get; set; }

    /// <summary>
    /// メッシュ
    /// </summary>
    [SerializeField]
    private List<MeshRenderer> meshRenderers = new();

    /// <summary>
    /// タグ
    /// </summary>
    [SerializeField]
    private List<LedData> leds = new();

    // Start is called before the first frame update
    protected override void Start()
    {
        meshRenderers = transform.GetComponentsInChildren<MeshRenderer>().ToList();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (var led in leds)
        {
            Destroy(led.material);
        }
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if (leds.Count > 0)
        {
            // タグ更新
            for (var i = 0; i < leds.Count; i++)
            {
                leds[i].tag.Value = GlobalScript.GetTagData(leds[i].tag);
            }
            // LEDセット
            var index = type == 0 ? 0 : leds.FindIndex(d => d.tag.Value == 1);
            foreach (var renderer in meshRenderers)
            {
                SetLed(renderer, leds[index < 0 ? 0 : index]);
            }
        }
    }

    private void SetLed(MeshRenderer renderer, LedData led)
    {
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", led.material.color);
        foreach (var mat in renderer.materials)
        {
            if (led.tag.Value == 1)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", led.material.color);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
            }
        }
        renderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        foreach (var led in leds)
        {
            Destroy(led.material);
        }
        leds = new List<LedData>();
        var ledSetting = (LedSetting)obj;
        type = ledSetting.type;
        foreach (var data in ledSetting.ledDatas)
        {
            var redColor = LedColor.Red;
            if (data.color == "Green")
            {
                redColor = LedColor.Green;
            }
            else if (data.color == "Yellow")
            {
                redColor = LedColor.Yellow;
            }
            else if (data.color == "Blue")
            {
                redColor = LedColor.Blue;
            }
            else if (data.color == "White")
            {
                redColor = LedColor.White;
            }
            var led = new LedData
            {
                color = data.color,
                tag = ScriptableObject.CreateInstance<TagInfo>(),
                material = Instantiate((Material)Resources.Load("Materials/Color/" + redColor.ToString()), transform)
            };
            led.tag.Database = unitSetting.Database;
            led.tag.MechId = unitSetting.mechId;
            led.tag.Tag = data.tag;
            leds.Add(led);
        }
    }
}
