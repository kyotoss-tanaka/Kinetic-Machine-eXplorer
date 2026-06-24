using UnityEngine;

/// <summary>
/// WebGLビルドで、このコンポーネントが付いた GameObject を非表示(SetActive false)にする。
/// 左下メニュー(アイコンバー)のルートに付ければ、WebGLでだけ消える。
/// エディタ・Windows・Android では表示されたまま（＝右下のPrefab選択等には付けないこと）。
/// </summary>
public class WebGlHide : MonoBehaviour
{
    [Tooltip("WebGLビルドで非表示にする")]
    [SerializeField] private bool hideOnWebGL = true;

    private void Awake()
    {
        if (hideOnWebGL && Application.platform == RuntimePlatform.WebGLPlayer)
        {
            gameObject.SetActive(false);
        }
    }
}
