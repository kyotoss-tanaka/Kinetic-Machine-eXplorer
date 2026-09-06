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
    protected List<LedData> leds;

    [SerializeField]
    protected List<int> values;

    /// <summary>
    /// 色を変える対象のマテリアル。
    /// renderer.materials はアクセスのたびに配列を確保し、初回はマテリアルの複製も起こすため、
    /// 毎フレーム触ってよいものではない。初期化時に1度だけ集める
    /// </summary>
    private Material[] targetMaterials;

    /// <summary>前回適用した色</summary>
    private Color appliedColor;

    /// <summary>前回適用した発光有無</summary>
    private bool appliedEmission;

    /// <summary>1度でも適用したか</summary>
    private bool hasApplied = false;

    /// <summary>シェーダプロパティID（文字列引きを避ける）</summary>
    private static readonly int emissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>シェーダプロパティID（文字列引きを避ける）</summary>
    private static readonly int baseColorId = Shader.PropertyToID("_BaseColor");

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
        if (leds == null)
        {
            leds = new();
        }
        if (values == null)
        {
            values = new();
        }
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
        CacheMaterials();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (leds != null)
        {
            foreach (var led in leds)
            {
                Destroy(led.material);
            }
        }
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if (leds == null)
        {
            leds = new();
        }
        if (leds.Count == 0)
        {
            return;
        }
        // タグ更新
        if (!isManual)
        {
            for (var i = 0; i < leds.Count; i++)
            {
                values[i] = GetTagValue(leds[i].name, ref leds[i]._tag);
            }
        }
        // 適用すべき色と発光を決める
        Color color;
        bool emission;
        if (type == 0)
        {
            emission = (values[0] == 1);
            color = leds[0].material.color;
        }
        else
        {
            var c = Color.black;
            for (var i = 0; i < leds.Count; i++)
            {
                c += (values[i] == 1) ? leds[i].material.color : Color.black;
            }
            var max = c.r;
            if (max < c.b)
            {
                max = c.b;
            }
            if (max < c.g)
            {
                max = c.g;
            }
            c = (max == 0) ? Color.black : new Color(c.r / max, c.g / max, c.b / max, 1);
            emission = (c != Color.black);
            color = emission ? c : Color.white;
        }
        // LEDの状態は滅多に変わらない。同じ値を入れ直してもシェーダキーワードの切替と
        // マテリアル更新が毎フレーム走るので、変化したときだけ適用する
        if (hasApplied && (emission == appliedEmission) && (color == appliedColor))
        {
            return;
        }
        hasApplied = true;
        appliedEmission = emission;
        appliedColor = color;
        SetColor(color, emission);
    }

    /// <summary>
    /// 色を変える対象のマテリアルを集める。
    /// 対象は各MeshRendererのマテリアルのうち、線描画用を除いたもの
    /// </summary>
    private void CacheMaterials()
    {
        var list = new List<Material>();
        foreach (var renderer in meshRenderers)
        {
            foreach (var mat in renderer.materials)
            {
                if (!mat.name.Contains("Default Line Material"))
                {
                    list.Add(mat);
                }
            }
        }
        targetMaterials = list.ToArray();
    }

    private void InitLedColor()
    {
        if (leds.Count > 0)
        {
            foreach (var renderer in meshRenderers)
            {
                foreach (var mat in renderer.materials)
                {
                    if (!mat.name.Contains("Default Line Material"))
                    {
                        mat.SetColor("_EmissionColor", leds[0].material.color);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 対象マテリアルへ色と発光を適用する
    /// </summary>
    private void SetColor(Color color, bool emission)
    {
        if (targetMaterials == null)
        {
            CacheMaterials();
        }
        var emissionColor = color * Mathf.LinearToGammaSpace(CommonDefine.EmissionIntensity);
        var baseColor = color * (emission ? 1f : 0.5f);
        foreach (var mat in targetMaterials)
        {
            if (emission)
            {
                mat.EnableKeyword("_EMISSION");
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
            }
            mat.SetColor(emissionColorId, emissionColor);
            mat.SetColor(baseColorId, baseColor);
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

        if (leds != null)
        {
            foreach (var led in leds)
            {
                Destroy(led.material);
            }
        }
        leds = new();
        values = new();
        hasApplied = false;
        var ledSetting = (LedSetting)obj;
        type = ledSetting.type;
        foreach (var data in ledSetting.ledDatas)
        {
            var ledColor = LedColor.Red;
            if (data.color == "Green")
            {
                ledColor = LedColor.Green;
            }
            else if (data.color == "Yellow")
            {
                ledColor = LedColor.Yellow;
            }
            else if (data.color == "Blue")
            {
                ledColor = LedColor.Blue;
            }
            else if (data.color == "White")
            {
                ledColor = LedColor.White;
            }
            var led = new LedData
            {
                color = data.color,
                name = data.tag,
                material = Instantiate((Material)Resources.Load("Materials/Color/" + ledColor.ToString()), transform)
            };
            leds.Add(led);
            values.Add(0);
        }
    }
}
