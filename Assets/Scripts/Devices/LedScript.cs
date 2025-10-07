using Meta.XR.ImmersiveDebugger.UserInterface;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
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
        public string name;
        public TagInfo _tag;
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

    [SerializeField]
    private List<decimal> values;

    /*
    /// <summary>
    /// ポストプロセス
    /// </summary>
    private Dictionary<MeshRenderer, PostProcessVolume> ppvs = new();
    private Dictionary<MeshRenderer, Bloom> blooms = new();
    */
    
    // Start is called before the first frame update
    protected override void Start()
    {
        meshRenderers = transform.GetComponentsInChildren<MeshRenderer>().ToList();

        /*
        // ポストプロセスセット
        foreach (var renderer in meshRenderers)
        {
            // ポストプロセスプロファイル作成
            PostProcessVolume ppv = renderer.transform.AddComponent<PostProcessVolume>();
            ppv.blendDistance = 0.01f;
            ppv.isGlobal = false;
            ppv.profile = ScriptableObject.CreateInstance<PostProcessProfile>();
            var bloom = ppv.profile.AddSettings<Bloom>();
            bloom.intensity.value = 10;
            bloom.intensity.overrideState = true;
            bloom.color.overrideState = true;
            ppvs[renderer] = ppv;
            blooms[renderer] = bloom;
        }
        */
        // 初期値セット
        InitLedColor();
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
            if (!isManual)
            {
                for (var i = 0; i < leds.Count; i++)
                {
                    values[i] = GetTagValue(leds[i].name, ref leds[i]._tag);
                }
            }

            // LEDセット
            if (type == 0)
            {
                foreach (var renderer in meshRenderers)
                {
                    SetColor(renderer, leds[0].material.color, values[0] == 1);
                }
            }
            else
            {
                Color c = Color.black;
                foreach (var led in leds)
                {
                    var index = leds.IndexOf(led);
                    c += values[index] == 1 ? led.material.color : Color.black;
                }
                c.a = 1;
                bool emission = c != Color.black;
                foreach (var renderer in meshRenderers)
                {
                    SetColor(renderer, emission ? c : Color.white, emission);
                }
            }
        }
    }

    private void InitLedColor()
    {
        if (leds.Count > 0)
        {
            foreach (var renderer in meshRenderers)
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat.shader.name != "Custom/Lines")
                    {
                        mat.SetColor("_EmissionColor", leds[0].material.color);
                    }
                }
            }
        }
    }

    private void SetColor(MeshRenderer renderer, Color color, bool emission)
    {
        /*
        ppvs[renderer].isGlobal = emission;
        blooms[renderer].color.value = color;
        */
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", color);
        foreach (var mat in renderer.materials)
        {
            if (mat.shader.name != "Custom/Lines")
            {
                if (emission)
                {
                    mat.EnableKeyword("_EMISSION");
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                }
                mat.SetColor("_EmissionColor", color * Mathf.LinearToGammaSpace(CommonDefine.EmissionIntensity));
                mat.SetColor("_Color", color * (emission ? Mathf.LinearToGammaSpace(CommonDefine.EmissionIntensity) : 0.5f));
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

        foreach (var led in leds)
        {
            Destroy(led.material);
        }
        leds = new();
        values = new();
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
                name = data.tag,
                material = Instantiate((Material)Resources.Load("Materials/Color/" + redColor.ToString()), transform)
            };
            leds.Add(led);
            values.Add(0);
        }
    }
}
