using Meta.XR.InputActions;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Reflection;
using System.Diagnostics;


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

    private bool mousePressed = false;
    private bool mouseWasPressedThisFrame = false;

#if UNITY_EDITOR
    private static Assembly m_assembly = Assembly.Load("UnityEditor.dll");
    private static System.Type m_type = m_assembly.GetType("UnityEditor.GameView");
    private static BindingFlags m_bindingAttr = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;
    private static MethodInfo m_snapZoomMethod = m_type.GetMethod("SnapZoom", m_bindingAttr);
    private static object[] m_parameters = new object[] { 1f };
#endif
    void Start()
    {
        initPosition = this.transform.position;
        initAngles = this.transform.eulerAngles;
        targetPosition = Vector3.zero;
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
            transform.position += transform.forward * delta * wheelSpeed;
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
                Vector3 pos = Camera.main.WorldToScreenPoint(targetPosition);
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
}
