using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class StickLocomotion: MonoBehaviour
{
    public Transform cameraTransform;
    public Transform playerRoot; // Snap Turn �p�Ƀ��[�g����]������
    public float moveSpeed = 2.0f;
    public float jumpForce = 5.0f;
    public float gravity = -9.81f;

    public float snapTurnAngle = 15f;
    public float snapTurnThreshold = 0.8f;
    public float snapTurnCooldown = 0.02f;

    private CharacterController controller;
    private float verticalVelocity;
    private float lastSnapTime;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        lastSnapTime = -snapTurnCooldown; // �N�������炷����]�\
    }

    void Update()
    {
        // ===== ���X�e�B�b�N�ňړ� =====
        Vector2 input = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();

        Vector3 move = (forward * input.y + right * input.x) * moveSpeed;

        // ===== �d�͂ƃW�����v =====
        if (controller.isGrounded)
        {
            verticalVelocity = -1f;
            if (OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) && OVRInput.GetDown(OVRInput.Button.One))
            {
                verticalVelocity = jumpForce;
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        move.y = verticalVelocity;
        controller.Move(move * Time.deltaTime);

        // ===== �E�X�e�B�b�N�� Snap Turn =====
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        if (Time.time - lastSnapTime >= snapTurnCooldown)
        {
            if (rightStick.x > snapTurnThreshold)
            {
                playerRoot.Rotate(0, snapTurnAngle, 0);
                lastSnapTime = Time.time;
            }
            else if (rightStick.x < -snapTurnThreshold)
            {
                playerRoot.Rotate(0, -snapTurnAngle, 0);
                lastSnapTime = Time.time;
            }
        }
    }
}
