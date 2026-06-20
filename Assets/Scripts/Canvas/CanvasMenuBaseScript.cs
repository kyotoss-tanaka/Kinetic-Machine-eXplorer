using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CanvasMenuBaseScript : KssBaseScript, IDragHandler
{
    /// <summary>
    /// 右
    /// </summary>
    protected bool isRight;
    /// <summary>
    /// クリック検知用
    /// </summary>
    protected GraphicRaycaster raycaster;
    /// <summary>
    /// ポインタイベントデータ
    /// </summary>
    private PointerEventData pointerEventData;
    /// <summary>
    /// イベントシステム
    /// </summary>
    private EventSystem eventSystem;
    /// <summary>
    /// キャンバス
    /// </summary>
    private Canvas canvas;
    /// <summary>
    /// 初期表示エリア
    /// </summary>
    private Rect initRect;
    /// <summary>
    /// 有効無効切り替えボタン
    /// </summary>
    private Button btnEnable;
    /// <summary>
    /// コンテンツ
    /// </summary>
    private List<Transform> objContents = new();
    /// <summary>
    /// 開く画像
    /// </summary>
    Sprite imgExpand;
    /// <summary>
    /// 閉じる画像
    /// </summary>
    Sprite imgShrink;
    /// <summary>
    /// 通常の幅
    /// </summary>
    private float normalWidth;
    /// <summary>
    /// 最小の幅
    /// </summary>
    private float minWidth;
    /// <summary>
    /// 幅
    /// </summary>
    private int lastWidth;
    /// <summary>
    /// 高さ
    /// </summary>
    protected int lastHeight;
    /// <summary>
    /// ダブルクリック用
    /// </summary>
    private float lastClickTime = 0f;
    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        canvas = this.transform.parent.GetComponent<Canvas>();
        raycaster = canvas.GetComponent<GraphicRaycaster>();
        eventSystem = EventSystem.current;
        initRect = ((RectTransform)transform).rect;
        btnEnable = GetComponentsInChildren<Button>().ToList().Find(d => d.name.Contains("Expand"));
        var title = btnEnable.gameObject.GetComponentInChildren<TextMeshProUGUI>().gameObject;

        // 画像取得
        Sprite[] sprites = Resources.LoadAll<Sprite>("Icons/sprits");
        imgExpand = sprites.FirstOrDefault(d => d.name == "icon_full-screen_24_Filled");
        imgShrink = sprites.FirstOrDefault(d => d.name == "icon_full-screen-exit_24_Filled");
        btnEnable.image.sprite = imgShrink;

        // 初期位置セット
        isRight = ((RectTransform)transform).anchorMax.x != 0;
        ((RectTransform)transform).anchoredPosition = new Vector2(0, 0);

        // 初期値セット
        lastWidth = (int)canvas.pixelRect.width;
        lastHeight = (int)canvas.pixelRect.height;
        minWidth = ((RectTransform)title.transform).sizeDelta.x + 40;
    }

    /// <summary>
    /// 更新
    /// </summary>
    protected override void Update()
    {
        base.Update();
        if ((lastWidth != (int)canvas.pixelRect.width) || (lastHeight != (int)canvas.pixelRect.height))
        {
            RenewPosition();
            lastWidth = (int)canvas.pixelRect.width;
            lastHeight = (int)canvas.pixelRect.height;
        }
        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            float time = Time.time;
            DetectClickedText(Mouse.current.rightButton.wasPressedThisFrame, time - lastClickTime < 0.3f);
            lastClickTime = time;
        }
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public virtual void SetEvents()
    {
        objContents = GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains("Contents"));
        ResetEvents();
        btnEnable.onClick.AddListener(expand_onClick);
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    public virtual void ResetEvents()
    {
        btnEnable.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// 表示/非表示
    /// </summary>
    private void expand_onClick()
    {
        var rect = (RectTransform)transform;
        var y = rect.anchoredPosition.y;// + rect.sizeDelta.y / 2;
        if (rect.sizeDelta.y == 30)
        {
            btnEnable.image.sprite = imgShrink;
            foreach (var obj in objContents)
            {
                obj.gameObject.SetActive(true);
            }
            rect.sizeDelta = new Vector2(initRect.width, initRect.height);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }
        else
        {
            btnEnable.image.sprite = imgExpand;
            foreach (var obj in objContents)
            {
                obj.gameObject.SetActive(false);
            }
            rect.sizeDelta = new Vector2(minWidth, 30);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        }
    }

    /// <summary>
    /// 移動
    /// </summary>
    /// <param name="eventData"></param>
    public void OnDrag(PointerEventData eventData)
    {
        var rectTransform = (RectTransform)transform;
        var x = rectTransform.anchoredPosition.x + eventData.delta.x;
        var y = rectTransform.anchoredPosition.y + eventData.delta.y;
        RenewPosition(x, y);
    }

    /// <summary>
    /// 位置を更新
    /// </summary>
    private void RenewPosition()
    {
        var rectTransform = (RectTransform)transform;
        RenewPosition(rectTransform.anchoredPosition.x, rectTransform.anchoredPosition.y);
    }

    /// <summary>
    /// 位置を更新
    /// </summary>
    private void RenewPosition(float x, float y)
    {
        var rectTransform = (RectTransform)transform;
        if (isRight)
        {
            if (x > 0)
            {
                x = 0;
            }
            else if (x < rectTransform.sizeDelta.x - canvas.pixelRect.width)
            {
                x = rectTransform.sizeDelta.x - canvas.pixelRect.width;
            }
        }
        else
        {
            if (x < 0)
            {
                x = 0;
            }
            else if (x > canvas.pixelRect.width - rectTransform.sizeDelta.x)
            {
                x = canvas.pixelRect.width - rectTransform.sizeDelta.x;
            }
        }
        if (y > 0)
        {
            y = 0;
        }
        else if (y < -canvas.pixelRect.height + rectTransform.sizeDelta.y)
        {
            y = -canvas.pixelRect.height + rectTransform.sizeDelta.y;
        }
        rectTransform.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>
    /// クリックイベント
    /// </summary>
    private void DetectClickedText(bool isRight, bool isDoubleClick)
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        pointerEventData = new PointerEventData(eventSystem)
        {
            position = mousePos
        };
        var results = new List<RaycastResult>();
        raycaster.Raycast(pointerEventData, results);
        foreach (var result in results)
        {
            ClickObject(result.gameObject, isRight, isDoubleClick);
        }
    }

    /// <summary>
    /// オブジェクトクリック
    /// </summary>
    /// <param name="name"></param>
    protected virtual void ClickObject(GameObject clickedObject, bool isRight, bool isDoubleClick)
    {
    }
}
