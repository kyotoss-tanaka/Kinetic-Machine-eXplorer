using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// パネルの右端・下端をドラッグしてリサイズするハンドルを追加する。
    /// タイムチャートパネルのルート RectTransform にアタッチして使用する。
    /// </summary>
    public class PanelResizeHandle : MonoBehaviour
    {
        [Header("リサイズ設定")]
        public float HandleSize = 8f;
        public float MinWidth = 200f;
        public float MinHeight = 100f;
        public float MaxWidth = 0f;
        public float MaxHeight = 0f;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")] static extern System.IntPtr LoadCursor(System.IntPtr h, int id);
        [DllImport("user32.dll")] static extern System.IntPtr SetCursor(System.IntPtr h);
        const int IDC_ARROW = 32512;
        const int IDC_SIZEWE = 32644;
        const int IDC_SIZENS = 32645;
        const int IDC_SIZENWSE = 32642;
#endif

        private RectTransform m_PanelRT;
        private Canvas m_Canvas;

        private bool m_ResizingRight;
        private bool m_ResizingBottom;
        private Vector2 m_DragStartPos;
        private Vector2 m_StartSize;

        // ホバー状態
        private bool m_HoverRight;
        private bool m_HoverBottom;
        private bool m_HoverCorner;

        private void Awake()
        {
            m_PanelRT = GetComponent<RectTransform>();
            m_Canvas = GetComponentInParent<Canvas>();
            BuildHandles();
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // 毎フレームカーソルを設定（Unityの上書きに対抗）
            int cursorId = IDC_ARROW;
            if (m_ResizingRight && m_ResizingBottom) cursorId = IDC_SIZENWSE;
            else if (m_ResizingRight) cursorId = IDC_SIZEWE;
            else if (m_ResizingBottom) cursorId = IDC_SIZENS;
            else if (m_HoverCorner) cursorId = IDC_SIZENWSE;
            else if (m_HoverRight) cursorId = IDC_SIZEWE;
            else if (m_HoverBottom) cursorId = IDC_SIZENS;

            SetCursor(LoadCursor(System.IntPtr.Zero, cursorId));
#endif
        }

        private void BuildHandles()
        {
            var right = CreateHandle("ResizeHandle_Right");
            var bottom = CreateHandle("ResizeHandle_Bottom");
            var corner = CreateHandle("ResizeHandle_Corner");

            // 右端
            right.anchorMin = new Vector2(1f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = new Vector2(-HandleSize, HandleSize);
            right.offsetMax = new Vector2(0f, -HandleSize);

            // 下端
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.offsetMin = new Vector2(HandleSize, -HandleSize);
            bottom.offsetMax = new Vector2(-HandleSize, 0f);

            // 右下コーナー
            corner.anchorMin = new Vector2(1f, 0f);
            corner.anchorMax = new Vector2(1f, 0f);
            corner.offsetMin = new Vector2(-HandleSize, -HandleSize);
            corner.offsetMax = new Vector2(0f, 0f);

            AddEvents(right, isRight: true, isBottom: false);
            AddEvents(bottom, isRight: false, isBottom: true);
            AddEvents(corner, isRight: true, isBottom: true);
        }

        private RectTransform CreateHandle(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(EventTrigger));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = true;
            return go.GetComponent<RectTransform>();
        }

        private void AddEvents(RectTransform rt, bool isRight, bool isBottom)
        {
            var trigger = rt.GetComponent<EventTrigger>();

            Add(trigger, EventTriggerType.PointerEnter, (_) =>
            {
                if (isRight && isBottom) m_HoverCorner = true;
                else if (isRight) m_HoverRight = true;
                else m_HoverBottom = true;
            });

            Add(trigger, EventTriggerType.PointerExit, (_) =>
            {
                if (isRight && isBottom) m_HoverCorner = false;
                else if (isRight) m_HoverRight = false;
                else m_HoverBottom = false;
            });

            Add(trigger, EventTriggerType.PointerDown, (data) =>
            {
                var ptr = (PointerEventData)data;
                m_ResizingRight = isRight;
                m_ResizingBottom = isBottom;
                m_DragStartPos = ptr.position;
                m_StartSize = new Vector2(m_PanelRT.rect.width, m_PanelRT.rect.height);
            });

            Add(trigger, EventTriggerType.Drag, (data) =>
            {
                if (!m_ResizingRight && !m_ResizingBottom) return;
                var ptr = (PointerEventData)data;
                float scl = m_Canvas != null ? m_Canvas.scaleFactor : 1f;
                Vector2 d = (ptr.position - m_DragStartPos) / scl;

                float newW = m_StartSize.x;
                float newH = m_StartSize.y;

                if (isRight)
                {
                    newW = Mathf.Max(m_StartSize.x + d.x, MinWidth);
                    if (MaxWidth > 0f) newW = Mathf.Min(newW, MaxWidth);
                }
                if (isBottom)
                {
                    newH = Mathf.Max(m_StartSize.y - d.y, MinHeight);
                    if (MaxHeight > 0f) newH = Mathf.Min(newH, MaxHeight);
                }

                m_PanelRT.sizeDelta = new Vector2(
                    isRight ? newW : m_PanelRT.sizeDelta.x,
                    isBottom ? newH : m_PanelRT.sizeDelta.y);
            });

            Add(trigger, EventTriggerType.PointerUp, (_) =>
            {
                m_ResizingRight = false;
                m_ResizingBottom = false;
            });
        }

        private static void Add(EventTrigger t, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(cb);
            t.triggers.Add(e);
        }
    }
}