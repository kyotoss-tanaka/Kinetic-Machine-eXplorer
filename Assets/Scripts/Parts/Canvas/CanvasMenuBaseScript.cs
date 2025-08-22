using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;

public class CanvasMenuBaseScript : KssBaseScript, IDragHandler
{
    /// <summary>
    /// �E
    /// </summary>
    protected bool isRight;
    /// <summary>
    /// �����\���G���A
    /// </summary>
    private Rect initRect;
    /// <summary>
    /// �L�������؂�ւ��{�^��
    /// </summary>
    private Button btnEnable;
    /// <summary>
    /// �R���e���c
    /// </summary>
    private GameObject objContents;
    /// <summary>
    /// �J���摜
    /// </summary>
    Sprite imgExpand;
    /// <summary>
    /// ����摜
    /// </summary>
    Sprite imgShrink;
    /// <summary>
    /// �J�n����
    /// </summary>
    protected override void Awake()
    {
        // �I�u�W�F�N�g�擾
        initRect = ((RectTransform)transform).rect;
        btnEnable = GetComponentsInChildren<Button>().ToList().Find(d => d.name.Contains("Expand"));
        objContents = GetComponentsInChildren<Transform>().ToList().Find(d => d.name.Contains("Contents")).gameObject;

        // �摜�擾
        Sprite[] sprites = Resources.LoadAll<Sprite>("Icons/sprits");
        imgExpand = sprites.FirstOrDefault(d => d.name == "icon_full-screen_24_Filled");
        imgShrink = sprites.FirstOrDefault(d => d.name == "icon_full-screen-exit_24_Filled");
        btnEnable.image.sprite = imgShrink;

        // �����ʒu�Z�b�g
        isRight = ((RectTransform)transform).anchorMax.x != 0;
        if (isRight)
        {
            // �E��
            ((RectTransform)transform).anchoredPosition = new Vector2(-initRect.width / 2, -initRect.height / 2);
        }
        else
        {
            // ����
            ((RectTransform)transform).anchoredPosition = new Vector2(initRect.width / 2, -initRect.height / 2);
        }
    }

    /// <summary>
    /// �C�x���g�Z�b�g
    /// </summary>
    public virtual void SetEvents()
    {
        ResetEvents();
        btnEnable.onClick.AddListener(expand_onClick);
    }

    /// <summary>
    /// �C�x���g�Z�b�g
    /// </summary>
    public virtual void ResetEvents()
    {
        btnEnable.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// �\��/��\��
    /// </summary>
    private void expand_onClick()
    {
        var rect = (RectTransform)transform;
        var y = rect.anchoredPosition.y + rect.sizeDelta.y / 2;
        if (rect.sizeDelta.y == 30)
        {
            btnEnable.image.sprite = imgShrink;
            objContents.SetActive(true);
            rect.sizeDelta = new Vector2(initRect.width, initRect.height);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y + initRect.y);
        }
        else
        {
            btnEnable.image.sprite = imgExpand;
            objContents.SetActive(false);
            rect.sizeDelta = new Vector2(initRect.width, 30);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y - 15);
        }
    }

    /// <summary>
    /// �ړ�
    /// </summary>
    /// <param name="eventData"></param>
    public void OnDrag(PointerEventData eventData)
    {
        var canvas = this.transform.parent.GetComponent<Canvas>();
        var rectTransform = (RectTransform)transform;
        var x = rectTransform.anchoredPosition.x + eventData.delta.x;
        var y = rectTransform.anchoredPosition.y + eventData.delta.y;
        if (isRight)
        {
            if (x > -rectTransform.sizeDelta.x / 2)
            {
                x = -rectTransform.sizeDelta.x / 2;
            }
            else if (x < -canvas.pixelRect.width + rectTransform.sizeDelta.x / 2)
            {
                x = -canvas.pixelRect.width + rectTransform.sizeDelta.x / 2;
            }
        }
        else
        {
            if (x < rectTransform.sizeDelta.x / 2)
            {
                x = rectTransform.sizeDelta.x / 2;
            }
            else if (x > canvas.pixelRect.width - rectTransform.sizeDelta.x / 2)
            {
                x = canvas.pixelRect.width - rectTransform.sizeDelta.x / 2;
            }
        }
        if (y > -rectTransform.sizeDelta.y / 2)
        {
            y = -rectTransform.sizeDelta.y / 2;
        }
        else if (y < -canvas.pixelRect.height + rectTransform.sizeDelta.y / 2)
        {
            y = -canvas.pixelRect.height + rectTransform.sizeDelta.y / 2;
        }
        rectTransform.anchoredPosition = new Vector2(x, y);
        /*

        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        for (int i = 0; i < 4; i++)
        {
            Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, worldCorners[i]);

            // ��ʓ����m�F
            if (screenPoint.x < 0 || screenPoint.x > Screen.width ||
                screenPoint.y < 0 || screenPoint.y > Screen.height)
            {
                // �����ł��͂ݏo�Ă���NG
                var x = screenPoint.x < 0 ? screenPoint.x : (screenPoint.x > Screen.width ? screenPoint.x - Screen.width : 0);
                var y = screenPoint.y < 0 ? screenPoint.y : (screenPoint.y > Screen.height ? screenPoint.y - Screen.height : 0);
                ((RectTransform)transform).anchoredPosition += new Vector2(-x, -y);
            }
        }
        */
    }
}
