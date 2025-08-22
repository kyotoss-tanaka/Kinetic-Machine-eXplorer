using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Windows;
using UnityEngine.InputSystem;
using Parameters;
using TMPro;
using Oculus.Platform;
using Application =UnityEngine.Application;
using Oculus.Interaction;
using UnityEngine.UI;
using System.Security.Principal;



#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView;
#endif

[ExecuteInEditMode]
public class MainProcess : KssBaseScript
{
    [SerializeField]
    List<GlobalScript.CbTagInfo> cbTags;

    private bool isVR { get { return (Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer); } }
    private InputSystem_Actions inputActions;
    private CameraController cameraController = null;
    private ParameterLoader parameterLoader = null;
    private RayInteractor rayInteractorL = null;
    private RayInteractor rayInteractorR = null;
    private KssBaseScript selectedScript = null;

    private bool isReloading = false;

    /// <summary>
    /// ������
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // �J�����ݒ�
        // var ovr = transform.parent.gameObject.GetComponentInChildren<OVRPlayerController>();
        // var camera = transform.parent.gameObject.GetComponentInChildren<Camera>();

        // �t���[�����[�g
        if (isVR)
        {
            // �A���h���C�h
            Application.targetFrameRate = 120;
            // VR��
            // camera.gameObject.SetActive(false);
        }
        else
        {
            // Windows
            Application.targetFrameRate = 120;
            // ovr.gameObject.SetActive(false);
        }

        // �f�[�^������
        inputActions = new InputSystem_Actions();
        var cameraControllers = FindObjectsByType<CameraController>(FindObjectsSortMode.None).ToList();
        if (cameraControllers.Count > 0)
        {
            cameraController = cameraControllers[0];
        }
        var parameterLoaders = FindObjectsByType<ParameterLoader>(FindObjectsSortMode.None).ToList();
        if (parameterLoaders.Count > 0)
        {
            parameterLoader = parameterLoaders[0];
        }
        var rayInteractors = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None).Where(d => d.transform.parent.parent.name == "LeftController").ToList();
        if (rayInteractors.Count > 0)
        {
            rayInteractorL = rayInteractors[0];
        }
        rayInteractors = FindObjectsByType<RayInteractor>(FindObjectsSortMode.None).Where(d => d.transform.parent.parent.name == "RightController").ToList();
        if (rayInteractors.Count > 0)
        {
            rayInteractorR = rayInteractors[0];
        }
    }

    protected override void OnEnable()
    {
        // �L�[�{�[�h�L����
        if (inputActions == null)
        {
            inputActions = new InputSystem_Actions();
            inputActions.Keyboard.Enable();
        }
        inputActions.Keyboard.Enable();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        inputActions.Keyboard.Disable();
    }

    protected override void Start()
    {
        base.Start();

        InitCallbackData();
    }

    protected override void Update()
    {
        base.Update();

        // �L�[�{�[�h����
        KeyUpdate();

        // �}�E�X����
        MouseUpdate();

        // �f�o�b�O�o��
        GlobalScript.DebugOut();
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        // �܂�Ԃ��e�X�g
        CallbackTest();
    }

    private void KeyUpdate()
    {
        Vector2 move = inputActions.Keyboard.Move.ReadValue<Vector2>();
        bool isControl = inputActions.Keyboard.ControlKey.IsPressed();
        bool isShift = inputActions.Keyboard.ShiftKey.IsPressed();

        // ���݂̃L�[��Ԏ擾
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            // R
            if (cameraController != null)
            {
                cameraController.SetInitPosition();
            }
        }
        else if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            // M
            if (cameraController != null)
            {
                cameraController.SetRoomPosition();
            }
        }
        else if (Keyboard.current.oKey.wasPressedThisFrame)
        {
            // O
            if (cameraController != null)
            {
                cameraController.InitCameraPosition();
            }
        }
        else if (Keyboard.current.lKey.wasPressedThisFrame)
        {
            // L
            if (parameterLoader != null)
            {
                isReloading = true;
                parameterLoader.ReloadActParameter();
                isReloading = false;
            }
        }
        else if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            // P
            if (parameterLoader != null)
            {
                isReloading = true;
                parameterLoader.ReloadParameter();
                InitCallbackData();
                isReloading = false;
            }
        }
        if (cameraController != null)
        {
            cameraController.MovePosition(move, isControl, isShift);
        }
    }

    private void MouseUpdate()
    {
        if (Application.isFocused)
        {
            // �J�����̃}�E�X���X�V
            if (cameraController != null)
            {
                // �}�E�X����X�V
                cameraController.MouseUpdate();
            }
        }

        var click = Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.leftButton.wasReleasedThisFrame;
        var left = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) || OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        var right = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) || OVRInput.GetUp(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        var down = Mouse.current.leftButton.wasPressedThisFrame || OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) || OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

        // ���N���b�N��Raycast(�I�u�W�F�N�g�I��)
        if (click || left || right)
        {
            GameObject clickedGameObject = null;
            if (left)
            {
                if (rayInteractorL.Interactable != null)
                {
                    clickedGameObject = rayInteractorL.Interactable.gameObject;
                }
            }
            else if (right)
            {
                if (rayInteractorR.Interactable != null)
                {
                    clickedGameObject = rayInteractorR.Interactable.gameObject;
                }
            }
            else
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                Ray ray = Camera.main.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out RaycastHit hit, 10, LayerMask.GetMask("Default"), QueryTriggerInteraction.Collide))
                {
                    clickedGameObject = hit.collider.gameObject;
                }
            }
            if (clickedGameObject != null)
            {
                var script = clickedGameObject.GetComponentInChildren<KssBaseScript>();
                if (script != null)
                {
                    if (down)
                    {
                        //�@�}�E�X�_�E��
                        selectedScript = script;
                        selectedScript.OnMouseDown();
                        if (cameraController != null)
                        {
                            cameraController.SetTargetPosition(clickedGameObject.transform.position);
                        }
                        // �Q�[���I�u�W�F�N�g�̖��O���o��
                        Debug.Log(clickedGameObject.name);
                    }
                    else if (selectedScript != null)
                    {
                        //�@�}�E�X�A�b�v
                        selectedScript.OnMouseUp();
                        selectedScript = null;
                    }
                }
                else
                {
                    if (cameraController != null)
                    {
                        cameraController.SetTargetPosition(clickedGameObject.transform.position);
                    }
                    // �Q�[���I�u�W�F�N�g�̖��O���o��
                    Debug.Log(clickedGameObject.name);
                }
            }
        }
        else
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            var script = selectedScript;
            if (Physics.Raycast(ray, out RaycastHit hit, 10, LayerMask.GetMask("Default"), QueryTriggerInteraction.Collide))
            {
                var mouseGameObject = hit.collider.gameObject;
                if (mouseGameObject != null)
                {
                    script = mouseGameObject.GetComponentInChildren<KssBaseScript>();
                }
            }
            if (selectedScript == null)
            {
                if (script != null)
                {
                    // ���񏈗�
                    selectedScript = script;
                    selectedScript.OnMouseEnter();
                }
            }
            else
            {
                if (script != selectedScript)
                {
                    if (script == null)
                    {
                        selectedScript.OnMouseExit();
                        selectedScript = null;
                    }
                    else
                    {
                        selectedScript.OnMouseExit();
                        selectedScript = script;
                        selectedScript.OnMouseEnter();
                    }
                }
                else
                {
                    selectedScript.OnMouseOver();
                }
            }
        }
    }

    private void InitCallbackData()
    {
        // �R�[���o�b�N�f�[�^������
        cbTags = new();
        foreach (var tag in GlobalScript.callbackTags)
        {
            GlobalScript.SetTagData(tag.output, 0);
            tag.output.Value = 0;
        }
    }

    /// <summary>
    /// �R�[���o�b�N�e�X�g
    /// </summary>
    public void CallbackTest()
    {
        if (!isReloading)
        {
            if (cbTags.Count == 0)
            {
                foreach (var tag in GlobalScript.callbackTags)
                {
                    tag.output.stopwatch = new();
                    tag.cntIn.stopwatch = new();
                    cbTags.Add(tag.output);
                    cbTags.Add(tag.cntIn);
                }
            }
            foreach (var tag in GlobalScript.callbackTags)
            {
                // �܂�Ԃ�
                if ((tag.input.Tag != "") && (tag.output.Tag != ""))
                {
                    var input = GlobalScript.GetTagData(tag.input);
                    var output = GlobalScript.GetTagData(tag.output);
                    if (input == output)
                    {
                        var next = input == 0 ? 1 : 0;
                        if ((tag.output.Value != next) || (tag.output.stopwatch.ElapsedMilliseconds > 5000))
                        {
                            GlobalScript.SetTagData(tag.output, next);
                            tag.output.SetLaps(tag.output.stopwatch.ElapsedMilliseconds);
                            tag.output.stopwatch.Restart();
                            tag.output.Value = next;
                        }
                    }
                    else
                    {
                    }
                }
                // �J�E���^
                if ((tag.cntIn.Tag != "") && (tag.cntOut.Tag != ""))
                {
                    var count = GlobalScript.GetTagData(tag.cntIn);
                    if (tag.cntIn.Value != count)
                    {
                        tag.cntIn.SetLaps(tag.cntIn.stopwatch.ElapsedMilliseconds);
                        tag.cntIn.stopwatch.Restart();
                        tag.cntIn.Value = count;
                    }
                    tag.cntOut.Value = (tag.cntOut.Value + 1) % 10000;
                    GlobalScript.SetTagData(tag.cntOut, tag.cntOut.Value);
                }
            }
        }
    }
}
