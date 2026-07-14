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

        // リロード対策: 前回このスクリプトが生成した ShapeBox（子）を先に破棄する（多重化防止）。
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var ch = transform.GetChild(i);
            if (ch.name == "ShapeBox")
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
}
