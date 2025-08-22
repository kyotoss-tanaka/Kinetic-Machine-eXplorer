using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.OpenXR.Input;
using static OVRPlugin;

public class ExMechScript : UseTagBaseScript
{
    [Serializable]
    class AxisInfo
    {
        [SerializeField]
        public GameObject model;
        [SerializeField]
        public List<GameObject> children;
    }

    /// <summary>
    /// ユニット設定
    /// </summary>
    [SerializeField]
    protected ExMechSetting exMechSetting;

    /// <summary>
    /// 機構タイプ 0:スライダークランク 1:ゼネバ機構
    /// </summary>
    [SerializeField]
    int mechType;

    /// <summary>
    /// 動作方向
    /// </summary>
    [SerializeField]
    Vector3 moveDir;

    /// <summary>
    /// 主軸(親)
    /// </summary>
    [SerializeField]
    AxisInfo mainAxis;

    /// <summary>
    /// 従動軸(自分)
    /// </summary>
    [SerializeField]
    AxisInfo drivenAxis;

    /// <summary>
    /// 中間軸
    /// </summary>
    [SerializeField]
    List<AxisInfo> intermediateAxis;

    /// <summary>
    /// アーム長L
    /// </summary>
    [SerializeField]
    float armL;

    /// <summary>
    /// アーム長M
    /// </summary>
    [SerializeField]
    float armM;

    /// <summary>
    /// 従動軸オフセット角度
    /// </summary>
    [SerializeField]
    float drivenOffset;

    /// <summary>
    /// 中間軸オフセット角度
    /// </summary>
    [SerializeField]
    List<float> intermediateOffset;

    /// <summary>
    /// マスク
    /// </summary>
    Vector3 maskDir1;

    /// <summary>
    /// マスク
    /// </summary>
    Vector3 maskDir2;

    /// <summary>
    /// アーム方向
    /// </summary>
    Vector3 armDir;

    /// <summary>
    /// 前回の姿勢保持
    /// </summary>
    Vector3 drivenEulerAngles;

    /// <summary>
    /// 前回の姿勢保持
    /// </summary>
    List<Vector3> intermediateEulerAngles;

    /// <summary>
    /// 従動軸のベース空間
    /// </summary>
    GameObject drivenBase;

    /// <summary>
    /// 中間軸のベース空間
    /// </summary>
    List<GameObject> intermediateBase;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        // 初期化処理
        Initialize();
    }

    /// <summary>
    /// 周期処理
    /// </summary>
    protected override void FixedUpdate()
    {
        // 駆動軸の座標系に変換
        var mainPos = Vector3.zero;
        var sliderPos = mainAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);

        // シャフト位置計算
        Vector3 point = armDir * armL + Vector3.Scale(sliderPos, maskDir2);

        // モデル1のローカル座標をワールド座標に変換
        intermediateAxis[0].model.transform.position = mainAxis.model.transform.TransformPoint(point);

        if (mechType == 0)
        {
            // スライダークランク機構
        }
        else if (mechType == 1)
        {
            // 従動軸角度設定
            sliderPos = drivenBase.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
            var mSliderPos = Vector3.Scale(sliderPos, maskDir1);
            var angle = GetAngle(Vector3.zero, mSliderPos) - drivenOffset;
            var eulerAngle = Vector3.Scale(new Vector3(angle, angle, angle), drivenBase.transform.localScale);
            drivenAxis.model.transform.localEulerAngles = Vector3.Scale(eulerAngle, maskDir2) + Vector3.Scale(drivenEulerAngles, maskDir1);

            // 中間軸角度設定
            var drivenPos = intermediateBase[0].transform.InverseTransformPoint(drivenAxis.model.transform.position);
            var mDrivenPos = Vector3.Scale(sliderPos, maskDir1);
            angle = GetAngle(Vector3.zero, mDrivenPos) - intermediateOffset[0];
            eulerAngle = Vector3.Scale(new Vector3(angle, angle, angle), intermediateBase[0].transform.localScale);
            intermediateAxis[0].model.transform.localEulerAngles = Vector3.Scale(eulerAngle, maskDir2) + Vector3.Scale(intermediateBase[0].transform.localEulerAngles, maskDir1);
        }
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        maskDir1 = new Vector3
        {
            x = moveDir.x == 0 ? 1 : 0,
            y = moveDir.y == 0 ? 1 : 0,
            z = moveDir.z == 0 ? 1 : 0
        };
        maskDir2 = new Vector3
        {
            x = moveDir.x != 0 ? 1 : 0,
            y = moveDir.y != 0 ? 1 : 0,
            z = moveDir.z != 0 ? 1 : 0
        };
        // 駆動軸の座標系に変換
        var mainPos = Vector3.zero;
        var sliderPos = mainAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
        var mMainPos = Vector3.Scale(mainPos, maskDir1);
        var mSliderPos = Vector3.Scale(sliderPos, maskDir1);
        if (mechType == 0)
        {
            // スライダークランク機構
        }
        else if (mechType == 1)
        {
            // ゼネバ機構
            armL = Vector3.Distance(mMainPos, mSliderPos);

            // 軸の方向取得
            var xp = Vector3.Distance(armL * Vector3.right.normalized, mSliderPos);
            var xm = Vector3.Distance(armL * Vector3.left.normalized, mSliderPos);
            var yp = Vector3.Distance(armL * Vector3.up.normalized, mSliderPos);
            var ym = Vector3.Distance(armL * Vector3.down.normalized, mSliderPos);
            var zp = Vector3.Distance(armL * Vector3.forward.normalized, mSliderPos);
            var zm = Vector3.Distance(armL * Vector3.back.normalized, mSliderPos);
            if (xp < 0.001f)
            {
                armDir = Vector3.right.normalized;
            }
            else if (xm < 0.001f)
            {
                armDir = Vector3.left.normalized;
            }
            else if (yp < 0.001f)
            {
                armDir = Vector3.up.normalized;
            }
            else if (ym < 0.001f)
            {
                armDir = Vector3.down.normalized;
            }
            else if (zp < 0.001f)
            {
                armDir = Vector3.forward.normalized;
            }
            else if (zm < 0.001f)
            {
                armDir = Vector3.back.normalized;
            }

            // 従動軸の座標系に変換
            sliderPos = drivenAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
            mSliderPos = Vector3.Scale(sliderPos, maskDir1);
            // 角度オフセット設定
            drivenOffset = GetAngle(Vector3.zero, mSliderPos);
            drivenEulerAngles = drivenAxis.model.transform.localEulerAngles;
            drivenBase = new GameObject("drivenBase");
            drivenBase.transform.parent = drivenAxis.model.transform.parent;
            drivenBase.transform.localPosition = drivenAxis.model.transform.localPosition;
            drivenBase.transform.localEulerAngles = drivenAxis.model.transform.localEulerAngles;
            drivenBase.transform.localScale = drivenAxis.model.transform.localScale;

            // 中間軸の座標系に変換
            var drivenPos = intermediateAxis[0].model.transform.InverseTransformPoint(drivenAxis.model.transform.position);
            var mDrivenPos = Vector3.Scale(sliderPos, maskDir1);
            // 角度オフセット設定
            intermediateOffset = new();
            intermediateOffset.Add(GetAngle(Vector3.zero, mDrivenPos));
            intermediateEulerAngles = new();
            intermediateEulerAngles.Add(intermediateAxis[0].model.transform.localEulerAngles);
            intermediateBase = new();
            intermediateBase.Add(new GameObject("intermediateBase"));
            intermediateBase[0].transform.parent = intermediateAxis[0].model.transform.parent;
            intermediateBase[0].transform.localPosition = intermediateAxis[0].model.transform.localPosition;
            intermediateBase[0].transform.localEulerAngles = intermediateAxis[0].model.transform.localEulerAngles;
            intermediateBase[0].transform.localScale = intermediateAxis[0].model.transform.localScale;
        }
    }

    /// <summary>
    /// 角度取得
    /// </summary>
    /// <param name="pos1"></param>
    /// <param name="pos2"></param>
    /// <returns></returns>
    private float GetAngle(Vector3 pos1, Vector3 pos2)
    {
        Vector2 A;
        Vector2 B;
        if ((moveDir == Vector3.right) || (moveDir == Vector3.left))
        {
            A = new Vector2(pos1.y, pos1.z);
            B = new Vector2(pos2.y, pos2.z);
        }
        else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
        {
            A = new Vector2(pos1.x, pos1.z);
            B = new Vector2(pos2.x, pos2.z);
        }
        else
        {
            A = new Vector2(pos1.x, pos1.y);
            B = new Vector2(pos2.x, pos2.y);
        }
        var dir = B - A;
        // ラジアンから角度に変換
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        // 必要なら0～360度に正規化
        if (angle < 0) angle += 360f;

        return angle;
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);
        exMechSetting = (ExMechSetting)obj;
        mechType = exMechSetting.type;
        switch (unitSetting.actionSetting.axis)
        {
            case 0:
                // X
                if (unitSetting.actionSetting.dir >= 0)
                {
                    moveDir = Vector3.right;
                }
                else
                {
                    moveDir = Vector3.left;
                }
                break;
            case 1:
                // Y
                if (unitSetting.actionSetting.dir >= 0)
                {
                    moveDir = Vector3.up;
                }
                else
                {
                    moveDir = Vector3.down;
                }
                break;

            case 2:
                // Z
                if (unitSetting.actionSetting.dir >= 0)
                {
                    moveDir = Vector3.forward;
                }
                else
                {
                    moveDir = Vector3.back;
                }
                break;
        }
        if (mechType == 0)
        {
        }
        else if (mechType == 1)
        {
            mainAxis = new AxisInfo
            {
                model = unitSetting.moveObject,
                children = new()
            };
            drivenAxis = new AxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                drivenAxis.children.Add(child.gameObject);
            }
            intermediateAxis = new();
            intermediateAxis.Add(new AxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            });
            foreach (var child in exMechSetting.datas[1].children)
            {
                intermediateAxis[0].children.Add(child.gameObject);
            }
        }
    }
}
