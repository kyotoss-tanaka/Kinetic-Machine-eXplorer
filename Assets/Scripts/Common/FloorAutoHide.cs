using UnityEngine;

/// <summary>
/// カメラが地面(Floor)より下にいる間、床の描画を自動で消す（見上げたときに機械の裏側が見えるように）。
/// 自己生成（シーン編集不要）。レンダラのみ無効化するためコライダ・物理挙動は変わらない。
/// </summary>
public class FloorAutoHide : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("FloorAutoHide");
        DontDestroyOnLoad(go);
        go.AddComponent<FloorAutoHide>();
    }

    private Renderer[] floorRenderers;
    private float floorTopY;
    private bool hidden;

    private void LateUpdate()
    {
        // Floorはシーンロード・再ロードで差し替わる可能性があるため、無効になったら取り直す
        if ((floorRenderers == null) || (floorRenderers.Length == 0) || (floorRenderers[0] == null))
        {
            var floor = GameObject.Find("Floor");
            if (floor == null)
            {
                return;
            }
            floorRenderers = floor.GetComponentsInChildren<Renderer>();
            if (floorRenderers.Length == 0)
            {
                return;
            }
            var bounds = floorRenderers[0].bounds;
            for (var i = 1; i < floorRenderers.Length; i++)
            {
                bounds.Encapsulate(floorRenderers[i].bounds);
            }
            floorTopY = bounds.max.y;
            hidden = false;
        }
        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }
        // カメラが床上面より下なら非表示（境界でのチラつき防止に少しヒステリシスを持たせる）
        var shouldHide = hidden
            ? cam.transform.position.y < floorTopY + 0.02f
            : cam.transform.position.y < floorTopY - 0.02f;
        if (shouldHide != hidden)
        {
            hidden = shouldHide;
            foreach (var r in floorRenderers)
            {
                if (r != null)
                {
                    r.enabled = !hidden;
                }
            }
        }
    }
}
