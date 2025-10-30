using Meta.XR.InputActions;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CanvasMenuAssemblyScript : CanvasMenuBaseScript
{
    private RectTransform sv;
    private RectTransform content;
    private TextMeshProUGUI baseText;

    private List<TextMeshProUGUI> viewTexts = new();

    private GameObject selectedObject;
    private TextMeshProUGUI selectedText;

    private GameObject selectedVisible;
    private List<GameObject> invisibleObjects = new();

    /// <summary>
    /// メインプロセス
    /// </summary>
    private MainProcess mainProcess;

    private bool isMoving = false;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // オブジェクト取得
        mainProcess = GameObject.FindObjectsByType<MainProcess>(FindObjectsSortMode.None)[0];
        sv = GetComponentsInChildren<RectTransform>().ToList().Find(d => d.name == "Scroll View");
        content = GetComponentsInChildren<RectTransform>().ToList().Find(d => d.name == "Content");
        baseText = GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "AssemblyText");
        baseText.gameObject.SetActive(false);
        SetAssembly(null);
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
        SetAssembly(null);
    }

    protected override void Update()
    {
        base.Update();
        if (Keyboard.current.vKey.wasPressedThisFrame)
        {
            // V 表示/非表示切り替え
            if (Keyboard.current.ctrlKey.isPressed)
            {
                foreach (var obj in invisibleObjects)
                {
                    obj.SetActive(true);
                }
                invisibleObjects.Clear();
            }
            else
            {
                if (selectedVisible != null)
                {
                    if (invisibleObjects.Contains(selectedVisible))
                    {
                        invisibleObjects.Remove(selectedVisible);
                    }
                    selectedVisible.SetActive(!selectedVisible.gameObject.activeSelf);
                    if (!selectedVisible.gameObject.activeSelf)
                    {
                        invisibleObjects.Add(selectedVisible);
                    }
                }
            }
            SetTextColor();
        }
        else if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            if (selectedVisible != null)
            {
                StartCoroutine(MoveAxis(selectedVisible, new Vector3(1, 0, 0), Keyboard.current.ctrlKey.isPressed));
            }
        }
        else if (Keyboard.current.yKey.wasPressedThisFrame)
        {
            if (selectedVisible != null)
            {
                StartCoroutine(MoveAxis(selectedVisible, new Vector3(0, 1, 0), Keyboard.current.ctrlKey.isPressed));
            }
        }
        else if (Keyboard.current.zKey.wasPressedThisFrame)
        {
            if (selectedVisible != null)
            {
                StartCoroutine(MoveAxis(selectedVisible, new Vector3(0, 0, 1), Keyboard.current.ctrlKey.isPressed));
            }
        }
    }

    /// <summary>
    /// イベントリセット
    /// </summary>
    public override void ResetEvents()
    {
        base.ResetEvents();
        /*
        viewCollision.onValueChanged.RemoveAllListeners();

        */
    }

    /// <summary>
    // オブジェクトクリック
    /// </summary>
    /// <param name="clickedObject"></param>
    protected override void ClickObject(GameObject clickedObject, bool isRight, bool isDoubleClick)
    {
        var text = clickedObject.GetComponent<TextMeshProUGUI>();
        if ((text != null) && (clickedObject.transform.parent.gameObject == content.gameObject))
        {
            if (!isRight)
            {
                if (selectedText != text)
                {
                    selectedText = text;
                    for (var obj = selectedObject; obj.transform.parent != null; obj = obj.transform.parent.gameObject)
                    {
                        var names = CommonFunction.GetScenePath(obj);
                        if (names.Count > 0)
                        {
                            names.Reverse();
                            names.RemoveAt(0);
                            var name = string.Join('\\', names);
                            if (text.name == name)
                            {
                                StartCoroutine(mainProcess.SelectObject(obj));
                                selectedVisible = obj;
                                break;
                            }
                        }
                    }
                }
                SetTextColor();
            }
            if (isDoubleClick)
            {
                if (!isRight)
                {
                    // 一瞬テキスト色を変える
                    StartCoroutine(TextEffect(text));
                    // クリップボードにコピー
                    GUIUtility.systemCopyBuffer = "*" + text.name;
                }
            }
        }
    }

    /// <summary>
    /// 一瞬色を変える
    /// </summary>
    /// <returns></returns>
    private IEnumerator TextEffect(TextMeshProUGUI text)
    {
        Color original = text.color;
        text.color = Color.yellow;
        yield return new WaitForSeconds(0.3f);
        text.color = original;
    }

    /// <summary>
    /// 軸動作
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="dir"></param>
    private IEnumerator MoveAxis(GameObject gameObject, Vector3 dir, bool isRotate)
    {
        if (!isMoving)
        {
            isMoving = true;
            var startPos = isRotate ? gameObject.transform.localEulerAngles : gameObject.transform.localPosition;
            if (isRotate)
            {
                yield return MoveTo(gameObject, isRotate, startPos + dir * 360f, 2f);
            }
            else
            {
                // 前半：0.5秒で+distance移動
                yield return MoveTo(gameObject, isRotate, startPos + dir * 1f, 1f);
                // 後半：0.5秒で元の位置に戻る
                yield return MoveTo(gameObject, isRotate, startPos, 1f);
            }
            isMoving = false;
        }
    }

    /// <summary>
    /// 動作処理
    /// </summary>
    /// <param name="targetPos"></param>
    /// <param name="time"></param>
    /// <returns></returns>
    private IEnumerator MoveTo(GameObject gameObject, bool isRotate, Vector3 targetPos, float time)
    {
        Vector3 initialPos = isRotate ? gameObject.transform.localEulerAngles : gameObject.transform.localPosition;
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            if (isRotate)
            {
                gameObject.transform.localEulerAngles = Vector3.Lerp(initialPos, targetPos, t / time);
            }
            else
            {
                gameObject.transform.localPosition = Vector3.Lerp(initialPos, targetPos, t / time);
            }
            yield return null;
        }
        if (isRotate)
        {
            gameObject.transform.localEulerAngles = targetPos; // 誤差防止
        }
        else
        {
            gameObject.transform.localPosition = targetPos; // 誤差防止
        }
    }

    /// <summary>
    /// アセンブリセット
    /// </summary>
    public void SetAssembly(GameObject gameObject)
    {
        selectedObject = gameObject;
        selectedVisible = gameObject;
        selectedText = null;
        foreach (var text in viewTexts)
        {
            try
            {
                Destroy(text.gameObject);
            }
            catch
            {
            }
        }
        viewTexts.Clear();
        var texts = selectedObject == null ? new() : CommonFunction.GetScenePath(selectedObject);
        texts.Reverse();
        if (texts.Count > 0)
        {
            texts.RemoveAt(0);
        }
        var fontSize = baseText.fontSize + 5;
        var height = texts.Count * fontSize + 10;
        var width = 0f;
        var names = new List<string>();
        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            names.Add(text);
            var obj = Instantiate(baseText.gameObject);
            var t = obj.gameObject.GetComponentInChildren<TextMeshProUGUI>();
            var rt = obj.GetComponent<RectTransform>();
            var left = i * 10;
            t.text = (i == 0 ? "" : "- ") + text;
            t.transform.parent = baseText.transform.parent;
            t.transform.localPosition = new Vector3(5 + left, -5 - (fontSize * i), 0);
            t.gameObject.SetActive(true);
            t.fontSharedMaterial.EnableKeyword("GLOW_ON");
            t.name = string.Join('\\', names);
            if (i == texts.Count - 1)
            {
                // 最終データは色変更
                t.color = new Color(1f, 1 / 2f, 0);
                selectedText = t;
            }
            viewTexts.Add(t);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            if (width < rt.rect.width + left)
            {
                width = rt.rect.width + left;
            }
        }
        var size = content.sizeDelta;
        size.x = width - 400 + 20;
        size.y = height;
        content.sizeDelta = size;
        size = sv.sizeDelta;
        size.y = height + (width >= 380 ? 20 : 0);
        sv.sizeDelta = size;
        size.y += 30;
        GetComponent<RectTransform>().sizeDelta = size;
    }

    /// <summary>
    /// テキストの色セット
    /// </summary>
    private void SetTextColor()
    {
        var invisible = false;
        foreach (var text in viewTexts)
        {
            if (text == selectedText)
            {
                text.color = new Color(1f, 1 / 2f, 0);
            }
            else
            {
                text.color = new Color(1f, 1f, 1f);
            }
            var delObj = new List<GameObject>();
            foreach (var obj in invisibleObjects)
            {
                if (invisible)
                {
                    text.color = text.color * 0.7f;
                }
                if (obj != null)
                {
                    var path = CommonFunction.GetScenePath(obj);
                    path.Reverse();
                    path.RemoveAt(0);
                    var name = string.Join('\\', path);
                    if (text.name == name)
                    {
                        text.color = text.color * 0.8f;
                        invisible = true;
                    }
                }
                else
                {
                    delObj.Add(obj);
                }
            }
            invisibleObjects.RemoveAll(d => delObj.Contains(d));
        }
    }
}
