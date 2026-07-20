using DnsClient.Protocol;
using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SwitchScript : KssBaseScript
{
    /// <summary>
    /// スイッチの動作タイプ
    /// </summary>
    public enum SwitchType
    {
        TagOutput,
        ObjectClear,
        ModelVisible
    }

    /// <summary>
    /// スイッチカラー
    /// </summary>
    public enum SwitchColor
    {
        Red, Green, Yellow, Blue, White
    }

    private SwitchSetting sw;

    /// <summary>
    /// オブジェクトクリアモード
    /// </summary>
    [SerializeField]
    private SwitchType switchType = SwitchType.TagOutput;

    /// <summary>
    /// オブジェクトクリアモード
    /// </summary>
    [SerializeField]
    private SwitchColor switchColor = SwitchColor.Red;

    /// <summary>
    /// タグ
    /// </summary>
    [SerializeField]
    private TagInfo Tag;

    /// <summary>
    /// B接点
    /// </summary>
    [SerializeField]
    private bool isB = false;

    /// <summary>
    /// オルタネートモード
    /// </summary>
    [SerializeField]
    private bool isAlternate = false;

    /// <summary>
    /// タグ名
    /// </summary>
    private string tagName = "";

    /// <summary>
    /// スイッチの状態
    /// </summary>
    private bool isOn = false;

    /// <summary>
    /// 初回フラグ
    /// </summary>
    private bool isFirst = true;

    /// <summary>
    /// 表示モデル
    /// </summary>
    private List<GameObject> lstVisible = new List<GameObject>();

    /// <summary>
    /// 操作をするトランスフォーム
    /// </summary>
    private Transform switchTransform;

    /// <summary>
    /// メッシュレンダラー
    /// </summary>
    private MeshRenderer meshRenderer;

    /// <summary>
    /// マテリアル
    /// </summary>
    private Material material;

    /// <summary>
    /// マテリアルカラー
    /// </summary>
    private Color matColor;

    /// <summary>
    /// VR用カメラ
    /// </summary>
    public Camera vrCamera;

    /// <summary>
    /// モデルをスイッチにするモード（"SwitchMain" が無い＝グループ設定の既存モデルを流用）。
    /// このとき押下アニメは行わず、モデル自身を発光のみで ON/OFF 表現する。
    /// </summary>
    private bool modelSwitch = false;

    /// <summary>
    /// モデルスイッチ時に発光させる Renderer 群（配下の全 Renderer）。
    /// </summary>
    private readonly List<Renderer> emissionRenderers = new List<Renderer>();

    /// <summary>
    /// モデルスイッチの ON/OFF 表現対象（マテリアルごとに、発光プロパティが有ればそれ、無ければ _BaseColor/_Color を使う）。
    /// </summary>
    private class GlowTarget
    {
        public Material mat;
        public bool emission;   // true=_EmissionColor で発光 / false=ベースカラー差し替え
        public string prop;     // emission=false のときの色プロパティ名
        public Color orig;      // OFF 復帰用の元色（emission=false のとき）
    }
    private readonly List<GlowTarget> glowTargets = new List<GlowTarget>();

    // Start is called before the first frame update
    protected override void Start()
    {
        var mains = transform.GetComponentsInChildren<Transform>(true).Where(d => d.name == "SwitchMain").ToList();
        if (mains.Count > 0)
        {
            // スイッチ専用プレハブ（従来）: SwitchMain メッシュを押下・発光させる。
            switchTransform = mains[0];
            meshRenderer = switchTransform.GetComponent<MeshRenderer>();
            if (meshRenderer != null) { meshRenderer.material = material; }
        }
        else
        {
            // モデルをスイッチにするモード: モデル自身（配下の全 Renderer）を発光。
            // ※クリック中継(relay)の付与は Start では行わない。collider の自動生成(CreateBoxCollider)は
            //   ロード後段のため、Start 時点ではまだ collider が無く relay が付かない。isLoaded 後の初回に付与する。
            modelSwitch = true;
            emissionRenderers.Clear();
            emissionRenderers.AddRange(GetComponentsInChildren<Renderer>(true));
        }
        var camera = GameObject.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(d => d.name == "CenterEyeAnchor").ToList();
        if (camera.Count > 0)
        {
            vrCamera = camera[0];
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Destroy(material);
    }

    protected override void MyFixedUpdate()
    {
        base.MyFixedUpdate();
        if (GlobalScript.isLoaded)
        {
            if (isFirst)
            {
                // 初回処理
                if (modelSwitch)
                {
                    // ロード完了後＝collider 自動生成(CreateBoxCollider)済み。配下の全 collider にクリック中継を付与。
                    AttachModelClickRelays();
                    // 発光/色変え対象（マテリアルとプロパティ）を確定（シェーダー差し替え後の状態で）。
                    SetupModelGlow();
                }
                if (switchType == SwitchType.ModelVisible)
                {
                    // アンドロイド時はモデルを消しておく
                    isOn = ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer));
                }
                isOn |= sw.value;
                RenewView();
                isFirst = false;
            }
        }
    }

    /// <summary>
    /// モデルスイッチの ON/OFF 表現対象を確定する。マテリアルごとに、発光プロパティ(_EmissionColor)が有れば発光、
    /// 無ければ _BaseColor/_Color をスイッチ色に差し替える方式を選ぶ（このプロジェクトの Shader Graph は _EmissionColor を持たない）。
    /// </summary>
    private void SetupModelGlow()
    {
        glowTargets.Clear();
        foreach (var r in emissionRenderers)
        {
            if (r == null) { continue; }
            var m = r.material;   // インスタンス化（このスイッチ分だけ変える）
            var g = new GlowTarget { mat = m };
            if (m.HasProperty("_EmissionColor"))
            {
                g.emission = true;
            }
            else if (m.HasProperty("_BaseColor"))
            {
                g.prop = "_BaseColor"; g.orig = m.GetColor("_BaseColor");
            }
            else if (m.HasProperty("_Color"))
            {
                g.prop = "_Color"; g.orig = m.GetColor("_Color");
            }
            else
            {
                continue;   // 発光も色変えもできないシェーダー
            }
            glowTargets.Add(g);
        }
    }

    /// <summary>
    /// モデルをスイッチにするモードで、配下の全 collider にクリック中継(SwitchClickRelay)を付与する。
    /// MainProcess は clickedGameObject.GetComponentInChildren&lt;KssBaseScript&gt;() でスクリプトを探すため、
    /// collider を持つ子オブジェクトに中継を付け、任意階層でも本体スイッチへクリックを転送する。
    /// collider 自動生成(CreateBoxCollider)後＝isLoaded の初回に呼ぶ。
    /// </summary>
    private void AttachModelClickRelays()
    {
        int n = 0;
        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            if (col.gameObject == gameObject) { continue; }   // root は MainProcess が本体を直接拾う
            if (col.GetComponent<SwitchClickRelay>() == null)
            {
                col.gameObject.AddComponent<SwitchClickRelay>().target = this;
                n++;
            }
        }
        if (n == 0)
        {
            Debug.LogWarning($"[Switch] モデルスイッチ '{name}' に collider が無くクリックできません（自動collider未生成/レイヤ違い等）。");
        }
    }

    /// <summary>
    /// 手でタップ
    /// </summary>
    /// <param name="other"></param>
    protected override void OnTriggerEnter(Collider other)
    {
        var parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.name.Contains("PinchPoint"))
            {
                OnMouseDown();
            }
        }
    }

    /// <summary>
    /// 手でタップ
    /// </summary>
    /// <param name="other"></param>
    protected override void OnTriggerExit(Collider other)
    {
        var parent = other.transform.parent;
        if (parent != null)
        {
            if (parent.name.Contains("PinchPoint"))
            {
                OnMouseUp();
            }
        }
    }

    /// <summary>
    /// マウスダウン
    /// </summary>
    public override void OnMouseDown()
    {
        if (isAlternate)
        {
            isOn = !isOn;
        }
        else
        {
            isOn = true;
        }
        RenewView();
    }

    /// <summary>
    /// マウスアップ
    /// </summary>
    public override void OnMouseUp()
    {
        if (!isAlternate)
        {
            isOn = false;
            RenewView();
        }
    }

    /// <summary>
    /// マウス外れ
    /// </summary>
    public override void OnMouseExit()
    {
        if (!isAlternate)
        {
            isOn = false;
            RenewView();
        }
    }

    /// <summary>
    /// スイッチ処理
    /// </summary>
    private void SwitchProcess()
    {
        if (switchType == SwitchType.TagOutput)
        {
            if (!GlobalScript.isSystemRecorder)
            {
                if (isB)
                {
                    // B接点
                    SetTagValue(tagName, ref Tag, isOn ? 0 : 1);
                }
                else
                {
                    // A接点
                    SetTagValue(tagName, ref Tag, isOn ? 1 : 0);
                }
            }
        }
        else if (switchType == SwitchType.ObjectClear)
        {
            foreach (var obj in GameObject.FindObjectsByType<ObjectScript>(FindObjectsSortMode.None))
            {
                Destroy(obj.gameObject);
            }
        }
        else if (switchType == SwitchType.ModelVisible)
        {
            foreach (var obj in lstVisible)
            {
                obj.gameObject.SetActive(!isOn);
            }
        }
    }

    private void RenewView()
    {
        if (modelSwitch)
        {
            // モデルをスイッチにするモード: 押下アニメはせず、ON でスイッチ色に（発光対応シェーダーは発光、非対応は _BaseColor 差し替え）。
            var emis = matColor * Mathf.LinearToGammaSpace(CommonDefine.EmissionIntensity);
            foreach (var g in glowTargets)
            {
                if (g.mat == null) { continue; }
                if (g.emission)
                {
                    if (isOn) { g.mat.EnableKeyword("_EMISSION"); } else { g.mat.DisableKeyword("_EMISSION"); }
                    g.mat.SetColor("_EmissionColor", isOn ? emis : Color.black);
                }
                else
                {
                    // 発光プロパティが無いシェーダー: ベースカラーをスイッチ色に（OFF で元色へ復帰）。
                    g.mat.SetColor(g.prop, isOn ? matColor : g.orig);
                }
            }
        }
        else
        {
            // スイッチの見た目を変える（従来: SwitchMain 発光＋押下移動）
            if (isOn)
            {
                meshRenderer.material.EnableKeyword("_EMISSION");
            }
            else
            {
                meshRenderer.material.DisableKeyword("_EMISSION");
            }
            meshRenderer.material.SetColor("_EmissionColor", matColor * Mathf.LinearToGammaSpace(CommonDefine.EmissionIntensity));
            meshRenderer.material.SetColor("_Color", matColor * (isOn ? Mathf.LinearToGammaSpace(CommonDefine.EmissionIntensity) : 1f));

            switchTransform.localPosition = new Vector3
            {
                x = 0,
                y = isOn ? 0.005f : 0.012f,
                z = 0
            };
        }

        // 処理
        SwitchProcess();
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        sw = (SwitchSetting)obj;
        isAlternate = sw.alternate;

        if (material != null)
        {
            Destroy(material);
        }
        if (sw.color == "Green")
        {
            switchColor = SwitchColor.Green;
        }
        else if (sw.color == "Yellow")
        {
            switchColor = SwitchColor.Yellow;
        }
        else if (sw.color == "Blue")
        {
            switchColor = SwitchColor.Blue;
        }
        else if (sw.color == "White")
        {
            switchColor = SwitchColor.White;
        }
        material = Instantiate((Material)Resources.Load("Materials/Color/" + switchColor.ToString()), switchTransform);
        material.DisableKeyword("_EMISSION");
        matColor = material.color;

        if (sw.mode == 0)
        {
            switchType = SwitchType.TagOutput;
            if ((sw.tag != null) && (sw.tag != ""))
            {
                tagName = sw.tag.Replace("-", "");
                isB = sw.tag[0] == '-';
            }
        }
        else if (sw.mode == 1)
        {
            switchType = SwitchType.ObjectClear;
        }
        else if (sw.mode == 2)
        {
            switchType = SwitchType.ModelVisible;
            if (sw.tag != null)
            {
                lstVisible = new();
                if (sw.tag == "")
                {
                    // 未入力の場合はプレハブモデル
                    lstVisible.AddRange(GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(d => d.name == "PrefabObjects").ToList());
                }
                else
                {
                    // カンマ区切りで非表示モデルを定義
                    foreach (var name in sw.tag.Split(","))
                    {
                        lstVisible.AddRange(GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(d => d.name == name).ToList());
                    }
                }
            }
        }
    }
}
