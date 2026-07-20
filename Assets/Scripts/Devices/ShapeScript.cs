using Parameters;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class ShapeScript : UseTagBaseScript
{
    /// <summary>
    /// 衝突検知
    /// </summary>
    /// <param name="other"></param>
    protected override void OnCollisionEnter(Collision other)
    {
        base.OnCollisionEnter(other);
        var obj = other.transform.GetComponentInParent<ObjectScript>();
        if (obj != null)
        {
            if (obj.transform.parent == null)
            {
                obj.transform.parent = transform;
            }
        }
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="robo"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
        
        var shape = (ShapeSetting)obj;

        // リロード対策: 前回このスクリプトが生成した ShapeBox/外枠（子）を先に破棄する（多重化防止）。
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var ch = transform.GetChild(i);
            if (ch.name.StartsWith("ShapeBox"))   // "ShapeBox"(塗り) と "ShapeBoxWire"(外枠) の両方
            {
                Destroy(ch.gameObject);
            }
        }

        if (shape.auto)
        {
            // 自動生成: 各メッシュに mesh-bounds の Collider を追加する（起動時 Collider の削除は AxisMotionBase 側で実施）。
            // isTrigger は選択用トリガとして true 固定（起動時の値は引き継がない）。
            foreach (var r in unitSetting.moveObject.GetComponentsInChildren<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;

                Bounds b = mf.sharedMesh.bounds;
                var box = r.gameObject.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
                box.isTrigger = false;
            }
        }
        // datas ごとに:
        //   create=true  … 見える四角の実体 ShapeBox（自前の BoxCollider 付き）を新規作成。
        //                   moveObject 側は触らないので、起動時の選択用 Collider はそのまま残る。
        //   create=false … transform に当たり判定(BoxCollider)のみを付ける（起動時 Collider を置換）。
        foreach (var s in shape.datas)
        {
            var center = new Vector3(s.center[0], s.center[1], s.center[2]);
            var size = new Vector3(s.size[0], s.size[1], s.size[2]);

            if (s.create)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "ShapeBox";
                cube.transform.SetParent(transform, false);
                cube.transform.localPosition = center;
                cube.transform.localScale = size;   // 付属の BoxCollider も実寸にスケールされる
                // マテリアルは URP シェーダから実行時生成（緑・半透明）。ゴースト/DCSゾーンと同方式で、
                // URP/Lit はシーン中で使われておりビルドでも strip されない（旧 Built-in Standard は不可視だった）。
                var rend = cube.GetComponent<Renderer>();
                if (rend != null)
                {
                    var mat = MakeShapeMaterial();
                    if (mat != null) { rend.sharedMaterial = mat; }
                }
                // DCSゾーンと同じ外枠（ワイヤフレーム）。塗りcube(scale=size)の子にすると線幅/位置が
                // 二重スケールされるので、transform直下の非スケール兄弟として size/2 の実寸で描く。
                AddWireframe(center, size);
            }
            else
            {
                var box = transform.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.center = center;
                box.size = size;
            }
        }
        // 親から設定されることを回避するためにセット
        /* 不必要？
        foreach (var mesh in this.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh.GetComponentInChildren<Collider>() == null)
            {
                var col = mesh.AddComponent<BoxCollider>();
                col.center = new Vector3();
                col.size = new Vector3();
            }
        }
        */
    }

    /// <summary>
    /// create=true の ShapeBox 用マテリアル（緑・半透明）。URP シェーダから実行時生成する。
    /// ゴースト([[CRX_30iA.MakeGhostMaterial]])/DCSゾーンと同方式。URP/Lit はシーンで使用済みのため
    /// ビルドでも strip されず可視（旧 Built-in Standard は URP ビルドで不可視だった）。
    /// </summary>
    private static Material MakeShapeMaterial()
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Unlit"); }
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        var col = new Color(0.2f, 1f, 0.35f, 0.35f);   // 緑・半透明
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        // URP transparent 設定（Surface=Transparent / alpha blend / ZWrite off）。
        if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); }
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    /// <summary>
    /// ShapeBox の外枠（DCSゾーンと同じワイヤフレーム）。transform 直下に非スケールで置き、
    /// 箱の12辺を1本の LineRenderer（重複3辺含む16点）で描く。破棄は "ShapeBoxWire" 名で cleanup 対象。
    /// </summary>
    private void AddWireframe(Vector3 center, Vector3 size)
    {
        var wireGo = new GameObject("ShapeBoxWire");
        wireGo.transform.SetParent(transform, false);
        wireGo.transform.localPosition = center;
        wireGo.transform.localRotation = Quaternion.identity;
        var lr = wireGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = false;
        lr.widthMultiplier = 0.004f;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        var lineCol = new Color(0.05f, 0.5f, 0.12f, 1f);   // 濃い緑の枠（塗りの明るい緑に対して縁が締まる）
        var mat = MakeLineMaterial(lineCol);
        if (mat != null) { lr.sharedMaterial = mat; lr.startColor = lineCol; lr.endColor = lineCol; }
        Vector3 h = size * 0.5f;
        Vector3[] c =
        {
            new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z),
            new Vector3(-h.x,  h.y, -h.z), new Vector3(h.x,  h.y, -h.z), new Vector3(h.x,  h.y, h.z), new Vector3(-h.x,  h.y, h.z),
        };
        int[] path = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };   // 全12辺を網羅（3辺重複）
        lr.positionCount = path.Length;
        for (int i = 0; i < path.Length; i++) { lr.SetPosition(i, c[path[i]]); }
    }

    /// <summary>線用の単色 URP マテリアル（取れなければ null）。</summary>
    private static Material MakeLineMaterial(Color col)
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) { sh = Shader.Find("Sprites/Default"); }
        if (sh == null) { return null; }
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) { m.SetColor("_BaseColor", col); }
        if (m.HasProperty("_Color")) { m.SetColor("_Color", col); }
        return m;
    }
}
