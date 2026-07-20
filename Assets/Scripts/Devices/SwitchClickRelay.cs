using UnityEngine;

/// <summary>
/// 「モデルをスイッチにする」モード用のクリック中継。
/// MainProcess はクリック時 clickedGameObject.GetComponentInChildren&lt;KssBaseScript&gt;() でスクリプトを探すため、
/// 配下(任意階層)の collider を持つ子オブジェクトにこれを付け、root の本体 SwitchScript へ OnMouse を転送する。
/// （EventPropagation は「直上の親」限定なので、任意階層に対応するため target を明示指定する専用中継にする）
/// </summary>
public class SwitchClickRelay : KssBaseScript
{
    /// <summary>転送先の本体スイッチ</summary>
    public SwitchScript target;

    public override void OnMouseDown()
    {
        base.OnMouseDown();
        if (target != null) { target.OnMouseDown(); }
    }

    public override void OnMouseUp()
    {
        base.OnMouseUp();
        if (target != null) { target.OnMouseUp(); }
    }

    public override void OnMouseExit()
    {
        base.OnMouseExit();
        if (target != null) { target.OnMouseExit(); }
    }
}
