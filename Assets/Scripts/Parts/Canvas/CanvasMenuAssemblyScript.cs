using Meta.XR.InputActions;
using Parameters;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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

    private CanvasMenuActUnitScript actScript;

    private CanvasMenuInfoScript menuInfoScript = null;

    private Dictionary<GameObject, Dictionary<Renderer, Material[]>> selectedRenderer = new();
    private List<Material> selectedMaterials = new();

    /// <summary>
    /// メインプロセス
    /// </summary>
    private MainProcess mainProcess;

    private bool isMoving = false;

    /// <summary>
    /// 動作ユニット
    /// </summary>
    private List<string> motionUnits = new();

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
        menuInfoScript = FindObjectsByType<CanvasMenuInfoScript>(FindObjectsSortMode.None).ToList()[0];

        SetAssembly(null);
    }

    /// <summary>
    /// 有効時
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();
        InputManager.Instance.RegisterKey(Key.V, HandleKey);
        InputManager.Instance.RegisterKey(Key.X, HandleKey);
        InputManager.Instance.RegisterKey(Key.Y, HandleKey);
        InputManager.Instance.RegisterKey(Key.Z, HandleKey);
    }

    /// <summary>
    /// 無効時
    /// </summary>
    protected override void OnDisable()
    {
        base.OnDisable();
        InputManager.Instance.UnregisterKey(Key.V, HandleKey);
        InputManager.Instance.UnregisterKey(Key.X, HandleKey);
        InputManager.Instance.UnregisterKey(Key.Y, HandleKey);
        InputManager.Instance.UnregisterKey(Key.Z, HandleKey);
    }

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();
    }

    protected override void Update()
    {
        base.Update();
    }

    /// <summary>
    /// キーイベント
    /// </summary>
    /// <param name="key"></param>
    private void HandleKey(Key key, bool value, bool isCtrl, bool isShift)
    {
        if (value)
        {
            if (key == Key.V)
            {
                // V 表示/非表示切り替え
                if (isCtrl)
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
            else if (key == Key.X)
            {
                if (selectedVisible != null)
                {
                    StartCoroutine(MoveAxis(selectedVisible, new Vector3(1, 0, 0), isShift));
                }
            }
            else if (key == Key.Y)
            {
                if (selectedVisible != null)
                {
                    StartCoroutine(MoveAxis(selectedVisible, new Vector3(0, 1, 0), isShift));
                }
            }
            else if (key == Key.Z)
            {
                if (selectedVisible != null)
                {
                    StartCoroutine(MoveAxis(selectedVisible, new Vector3(0, 0, 1), isShift));
                }
            }
        }
    }

    /// <summary>
    /// イベントセット
    /// </summary>
    /// <param name="uiActUnitInfo"></param>
    public void SetEvents(GameObject uiActUnitInfo)
    {
        actScript = uiActUnitInfo.GetComponent<CanvasMenuActUnitScript>();
        SetAssembly(null);
        base.SetEvents();
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
                                StartCoroutine(SelectObject(obj));
                                selectedVisible = obj;
                                if ((motionUnits.Count > 0) && (actScript != null))
                                {
                                    // ユニット選択
                                    actScript.SelectUnit(obj.name);
                                    menuInfoScript.SetAssemblyObject(obj, true);
                                }
                                else
                                {
                                    // 部品選択
                                    actScript.SelectParts(obj);
                                    menuInfoScript.SetAssemblyObject(obj, true);
                                }
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
        // 選択解除
        if (gameObject == null)
        {
            ClearSelected();
        }

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
        // 階層取得
        var texts = selectedObject == null ? new() : CommonFunction.GetScenePath(selectedObject);
        texts.Reverse();
        if (texts.Count > 0)
        {
            texts.RemoveAt(0);
        }
        // 動作ユニット取得
        var motions = gameObject == null ? new List<AxisMotionBase>() : gameObject.GetComponentsInParent<AxisMotionBase>().ToList();
        motionUnits = motions.Select(d => d.name).ToList();
        var fontSize = baseText.fontSize + 5;
        var width = 0f;
        var names = new List<string>();
        var index = 0;
        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            names.Add(text);
            if ((motionUnits.Count > 0) && !motionUnits.Contains(text))
            {
                continue;
            }
            var obj = Instantiate(baseText.gameObject);
            var t = obj.gameObject.GetComponentInChildren<TextMeshProUGUI>();
            var rt = obj.GetComponent<RectTransform>();
            var left = index * 10;
            t.text = (index == 0 ? "" : "- ") + text + (motionUnits.Count > 0 ? "(Motion)" : "");
            t.transform.SetParent(baseText.transform.parent);
            t.transform.localPosition = new Vector3(5 + left, -5 - (fontSize * index), 0);
            t.gameObject.SetActive(true);
//            t.fontSharedMaterial.EnableKeyword("GLOW_ON");
            t.name = string.Join('\\', names);
            viewTexts.Add(t);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            if (width < rt.rect.width + left)
            {
                width = rt.rect.width + left;
            }
            index++;
        }
        var height = index * fontSize + 10;
        var size = content.sizeDelta;
        size.x = width - 400 + 20;
        size.y = height;
        content.sizeDelta = size;
        size = sv.sizeDelta;
        size.y = height + (width >= 380 ? 20 : 0);
        sv.sizeDelta = size;
        size.y += 30;
        GetComponent<RectTransform>().sizeDelta = size;
        menuInfoScript.btnAsm_Visible(index != 0);
//        this.gameObject.SetActive();
        if (viewTexts.Count > 0)
        {
            // 一番最後のデータをクリック
            ClickObject(viewTexts[viewTexts.Count - 1].gameObject, false, false);
        }
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

    /// <summary>
    /// 選択解除
    /// </summary>
    private void ClearSelected()
    {
        selectedMaterials.RemoveAll(d => d == null);
        foreach (var mat in selectedMaterials)
        {
            Destroy(mat);
        }
        selectedMaterials.Clear();
        foreach (var gameObject in selectedRenderer)
        {
            foreach (var renderer in gameObject.Key.GetComponentsInChildren<Renderer>())
            {
                if (selectedRenderer[gameObject.Key].ContainsKey(renderer))
                {
                    renderer.sharedMaterials = selectedRenderer[gameObject.Key][renderer];
                }
            }
        }
        selectedRenderer.Clear();
    }

    /// <summary>
    /// オブジェクトを選択する
    /// </summary>
    /// <param name="gameObject"></param>
    public IEnumerator SelectObject(GameObject gameObject)
    {
        // 一旦選択解除
        ClearSelected();

        if (gameObject != null)
        {
            // 再選択
            selectedRenderer[gameObject] = new Dictionary<Renderer, Material[]>();
            var renderers = gameObject.GetComponentsInChildren<Renderer>().ToList();
            foreach (var renderer in renderers)
            {
                selectedRenderer[gameObject][renderer] = renderer.sharedMaterials;
                foreach (Material mat in renderer.materials)
                {
                    if (mat != null)
                    {
                        if (mat.name.Contains("Default Line Material"))
                        {
                            mat.SetColor("_BaseColor", new Color(1f, 1 / 2f, 0, 1f));
                            selectedMaterials.Add(mat);
                        }
                    }
                }
            }
        }
        yield return null;
    }
}
