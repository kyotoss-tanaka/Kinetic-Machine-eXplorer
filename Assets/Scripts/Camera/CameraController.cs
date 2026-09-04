using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Reflection;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CameraController : MonoBehaviour
{
    [SerializeField]
    private bool cameraEnable;

    [SerializeField, Range(0.1f, 10f)]
    private float wheelSpeed = 1f;

    [SerializeField, Range(0.1f, 10f)]
    private float moveSpeed = 0.1f;

    [SerializeField, Range(0.1f, 10f)]
    private float rotateSpeed = 0.1f;

    private Vector3 preMousePos;

    private Vector3 initPosition;
    private Vector3 initAngles;
    private Vector3 targetPosition;

    private float focusDistance = 0.5f;

    private bool mousePressed = false;
    private bool mouseWasPressedThisFrame = false;

#if UNITY_EDITOR
    private static Assembly m_assembly = Assembly.Load("UnityEditor.dll");
    private static System.Type m_type = m_assembly.GetType("UnityEditor.GameView");
    private static BindingFlags m_bindingAttr = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    private static MethodInfo m_snapZoomMethod = m_type.GetMethod("SnapZoom", m_bindingAttr);
    private static object[] m_parameters = new object[] { 1f };
#endif

    /// <summary>
    /// キーボードの状態
    /// </summary>
//    private bool isControl, isShift, isMouseLeft;

    /// <summary>
    /// 各種ボタン状態
    /// </summary>
    private bool isMouseRight, isMouseMiddle;

    /// <summary>
    /// 開始処理
    /// </summary>
    void Start()
    {
        initPosition = this.transform.position;
        initAngles = this.transform.eulerAngles;
        targetPosition = Vector3.zero;
    }

    /// <summary>
    /// 有効時
    /// </summary>
    void OnEnable()
    {
        InputManager.Instance.RegisterKey(Key.F, HandleKey);
        InputManager.Instance.RegisterKey(Key.R, HandleKey);
        InputManager.Instance.RegisterKey(Key.M, HandleKey);
        InputManager.Instance.RegisterKey(Key.O, HandleKey);
        InputManager.Instance.RegisterKey(Key.LeftCtrl, HandleKey);
        InputManager.Instance.RegisterKey(Key.RightCtrl, HandleKey);
        InputManager.Instance.RegisterKey(Key.LeftShift, HandleKey);
        InputManager.Instance.RegisterKey(Key.RightShift, HandleKey);
        InputManager.Instance.RegisterMouseDown(MouseDownEvent);
        InputManager.Instance.RegisterMouseUp(MouseUpEvent);
        InputManager.Instance.RegisterMouseWheel(MouseWheelEvent);
        InputManager.Instance.RegisterMouseMove(MouseMoveEvent);
    }

    /// <summary>
    /// 無効時
    /// </summary>
    void OnDisable()
    {
        InputManager.Instance.UnregisterKey(Key.F, HandleKey);
        InputManager.Instance.UnregisterKey(Key.R, HandleKey);
        InputManager.Instance.UnregisterKey(Key.M, HandleKey);
        InputManager.Instance.UnregisterKey(Key.O, HandleKey);
        InputManager.Instance.UnregisterKey(Key.LeftCtrl, HandleKey);
        InputManager.Instance.UnregisterKey(Key.RightCtrl, HandleKey);
        InputManager.Instance.UnregisterKey(Key.LeftShift, HandleKey);
        InputManager.Instance.UnregisterKey(Key.RightShift, HandleKey);
        InputManager.Instance.UnregisterMouseDown(MouseDownEvent);
        InputManager.Instance.UnregisterMouseUp(MouseUpEvent);
        InputManager.Instance.UnregisterMouseWheel(MouseWheelEvent);
        InputManager.Instance.UnregisterMouseMove(MouseMoveEvent);
    }

    /// <summary>
    /// キーイベント
    /// </summary>
    /// <param name="key"></param>
    private void HandleKey(Key key, bool value, bool isCtrl, bool isShift)
    {
        if (value)
        {
            // ON処理
            if (key == Key.F)
            {
                if (isShift)
                {
                    // Shift+F: 選択オブジェクトの原点を回転中心にする（カメラは動かさない）
                    if (GlobalScript.selectedObject != null)
                    {
                        SetTargetPosition(GlobalScript.selectedObject.transform.position);
                    }
                }
                else
                {
                    // F: 選択オブジェクトへフォーカス（回転中心変更＋カメラ移動）
                    FocusTo();
                }
            }
            else if (key == Key.R)
            {
                // R
                SetInitPosition();
            }
            else if (key == Key.M)
            {
                // M
                SetRoomPosition();
            }
            else if (key == Key.O)
            {
                // O
                InitCameraPosition();
            }
            else if ((key == Key.LeftCtrl) || (key == Key.RightCtrl))
            {
//                isControl = true;
            }
            else if ((key == Key.LeftShift) || (key == Key.RightShift))
            {
                isShift = true;
            }
        }
        else
        {
            if ((key == Key.LeftCtrl) || (key == Key.RightCtrl))
            {
//                isControl = false;
            }
            else if ((key == Key.LeftShift) || (key == Key.RightShift))
            {
                isShift = false;
            }
        }
    }

    /// <summary>
    /// マウスダウンイベント
    /// </summary>
    /// <param name="button"></param>
    private void MouseDownEvent(InputManager.MouseButton button, Vector2 mousePos)
    {
        if (button == InputManager.MouseButton.LeftButton)
        {
//            isMouseLeft = true;
        }
        else if (button == InputManager.MouseButton.RightButton)
        {
            isMouseRight = true;
        }
        else if (button == InputManager.MouseButton.MiddleButton)
        {
            isMouseMiddle = true;
        }
    }

    /// <summary>
    /// マウスアップイベント
    /// </summary>
    /// <param name="button"></param>
    private void MouseUpEvent(InputManager.MouseButton button, Vector2 mousePos)
    {
        if (button == InputManager.MouseButton.LeftButton)
        {
//            isMouseLeft = false;
        }
        else if (button == InputManager.MouseButton.RightButton)
        {
            isMouseRight = false;
        }
        else if (button == InputManager.MouseButton.MiddleButton)
        {
            isMouseMiddle = false;
        }
    }

    /// <summary>
    /// マウスホイールイベント
    /// </summary>
    /// <param name="mousePos"></param>
    private void MouseWheelEvent(Vector2 scrollDelta)
    {
        float dist = Vector3.Distance(transform.position, targetPosition);
        float speedFactor = Mathf.Clamp01(dist / 10f);  // 距離10以上なら最大速、近いときは遅く
        float moveSpeed = wheelSpeed * speedFactor;
        transform.position += transform.forward * scrollDelta.y * moveSpeed;
    }

    /// <summary>
    /// マウス移動イベント
    /// </summary>
    /// <param name="mousePos"></param>
    private void MouseMoveEvent(Vector2 mousePos, Vector2 moveDelta)
    {
        if (moveDelta.magnitude < Vector3.kEpsilon)
        {
            return;
        }
        if (isMouseMiddle)
        {
            Vector3 pos = CommonFunction.MainCamera.WorldToScreenPoint(targetPosition);
            transform.Translate(-moveDelta * 0.01f * moveSpeed * pos.z / 5);
        }
        else if (isMouseRight)
        {
            CameraRotate(new Vector2(-moveDelta.y, moveDelta.x) * rotateSpeed);
        }
    }

    /// <summary>
    /// 初期位置
    /// </summary>
    public void SetInitPosition()
    {
        this.transform.position = initPosition;
        this.transform.eulerAngles = initAngles;
        targetPosition = Vector3.zero;
    }

    /// <summary>
    /// 精神と時の部屋の位置
    /// </summary>
    public void SetRoomPosition()
    {
        this.transform.position = new Vector3(-430, 3, 0);
        this.transform.eulerAngles = new Vector3(0, 270, 0);
        targetPosition = Vector3.zero;
    }

    /// <summary>
    /// 視点の初期化
    /// </summary>
    public void InitCameraPosition()
    {
        targetPosition = initPosition;
    }

    /// <summary>
    /// 視点の設定
    /// </summary>
    public void SetTargetPosition(Vector3 targetPosition)
    {
        this.targetPosition = targetPosition;
    }

    /// <summary>
    /// 上移動
    /// </summary>
    /// <param name="isControl"></param>
    public void MovePosition(Vector2 move, bool isControl, bool isShift)
    {
        // 上下
        if (move.y > 0)
        {
            if (isShift)
            {
                transform.Translate(Vector3.up * Time.deltaTime * moveSpeed);
            }
            else if (isControl)
            {
                CameraRotate(new Vector2(10, 0) * rotateSpeed);
            }
            else
            {
                transform.Translate(Vector3.forward * Time.deltaTime * moveSpeed);
            }
        }
        else if (move.y < 0)
        {
            if (isShift)
            {
                transform.Translate(Vector3.down * Time.deltaTime * moveSpeed);
            }
            else if (isControl)
            {
                CameraRotate(new Vector2(-10, 0) * rotateSpeed);
            }
            else
            {
                transform.Translate(Vector3.back * Time.deltaTime * moveSpeed);
            }
        }
        // 左右
        if (move.x < 0)
        {
            if (isControl)
            {
                CameraRotate(new Vector2(0, 10) * rotateSpeed);
            }
            else
            {
                transform.Translate(Vector3.left * Time.deltaTime * moveSpeed);
            }
        }
        else if (move.x > 0)
        {
            if (isControl)
            {
                CameraRotate(new Vector2(0, -10) * rotateSpeed);
            }
            else
            {
                transform.Translate(Vector3.right * Time.deltaTime * moveSpeed);
            }
        }
    }

    public void MouseUpdate()
    {
        var mouse = Mouse.current;
#if UNITY_EDITOR
        cameraEnable = EditorApplication.isPlaying || Keyboard.current.ctrlKey.isPressed;
        if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed)
        {
            if (mousePressed)
            {
                mouseWasPressedThisFrame = false;
            }
            else
            {
                mousePressed = true;
                mouseWasPressedThisFrame = true;
            }
        }
        else
        {
            mousePressed = false;
            mouseWasPressedThisFrame = false;
        }
        var gameView = EditorWindow.GetWindow(m_type);
        if (gameView != null)
        {
            m_snapZoomMethod.Invoke(gameView, m_parameters);
        }
#else
        cameraEnable = true;
        mouseWasPressedThisFrame = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
#endif
        Vector2 scrollDelta = Mouse.current.scroll.ReadValue();
        float scrollWheel = scrollDelta.y;
        if (scrollWheel != 0.0f)
        {
            MouseWheel(scrollWheel);
        }

        // ボタンが押されたら現在のマウス位置を保存
        if (mouseWasPressedThisFrame)
        {
            preMousePos = mouse.position.ReadValue();
        }

        // ドラッグ処理（あなたの既存関数に合わせて）
        MouseDrag(mouse.position.ReadValue());
    }

    private void MouseWheel(float delta)
    {
        if (cameraEnable)
        {
            float dist = Vector3.Distance(transform.position, targetPosition);
            float speedFactor = Mathf.Clamp01(dist / 10f);  // 距離10以上なら最大速、近いときは遅く
            float moveSpeed = wheelSpeed * speedFactor;
            transform.position += transform.forward * delta * moveSpeed;
        }
    }

    private void MouseDrag(Vector3 mousePos)
    {
        Vector3 diff = mousePos - preMousePos;

        if (diff.magnitude < Vector3.kEpsilon)
            return;

        if (cameraEnable)
        {
            if (Mouse.current.middleButton.isPressed)
            {
                Vector3 pos = CommonFunction.MainCamera.WorldToScreenPoint(targetPosition);
                transform.Translate(-diff * 0.01f * moveSpeed * pos.z / 5);
            }
            else if (Mouse.current.rightButton.isPressed)
            {
                CameraRotate(new Vector2(-diff.y, diff.x) * rotateSpeed);
            }
        }
        preMousePos = mousePos;
    }

    public void CameraRotate(Vector2 angle)
    {
        transform.RotateAround(targetPosition, transform.right, angle.x);
        transform.RotateAround(targetPosition, Vector3.up, angle.y);
    }

    /// <summary>
    /// フォーカス
    /// </summary>
    /// <param name="target"></param>
    public void FocusTo(Transform target)
    {
        focusDistance = focusDistance <= 1f ? 3f : 1f;
        // カメラの向きは維持したまま、ターゲットを中央に
        Vector3 forward = transform.forward;
        transform.position = target.position - forward * focusDistance;

        SetTargetPosition(target.position);
    }

    /// <summary>
    /// フォーカス
    /// </summary>
    public void FocusTo()
    {
        // 通常オブジェクトが未選択なら、調整パネル(F9)で選択中の対象へフォーカスする
        var position = targetPosition;
        if (GlobalScript.selectedObject != null)
        {
            position = GlobalScript.selectedObject.transform.position;
        }
        else if (WorkAdjustPanel.TryGetSelectedPosition(out var adjustPos))
        {
            position = adjustPos;
        }
        focusDistance = focusDistance <= 1f ? 3f : 1f;
        Vector3 forward = transform.forward;
        transform.position = position - forward * focusDistance;

        SetTargetPosition(position);
    }

    /// <summary>
    /// 回転中心(targetPosition)を pivot にし、カメラを frontDir 方向(=正面)へ dist 離して pivot を注視する。
    /// 経路計画パネル表示/機種切替時に、対象ロボットの正面へ寄せて回転中心を TCP に合わせる用途。
    /// </summary>
    public void MoveToFront(Vector3 pivot, Vector3 frontDir, float dist)
    {
        targetPosition = pivot;
        Vector3 f = frontDir.sqrMagnitude > 1e-6f ? frontDir.normalized : Vector3.forward;
        transform.position = pivot + f * dist;
        Vector3 look = pivot - transform.position;
        if (look.sqrMagnitude > 1e-6f) { transform.rotation = Quaternion.LookRotation(look, Vector3.up); }
    }
}
