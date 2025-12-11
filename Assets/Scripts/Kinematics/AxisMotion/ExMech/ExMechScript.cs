using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst.Intrinsics;
using UnityEngine;
using UnityEngine.SocialPlatforms;

public class ExMechScript : UseTagBaseScript
{
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
    /// 作業オブジェクト
    /// </summary>
    GameObject workSpace;

    /// <summary>
    /// 初期角度
    /// </summary>
    private Vector3 initAngle = Vector3.zero;

    /// <summary>
    /// 機構情報
    /// </summary>
    private ExMechInfo mechInfo;

    /// <summary>
    /// 親モデル
    /// </summary>
    public GameObject parentModel;

    /// <summary>
    /// 現在位置
    /// </summary>
    public Vector3 NowPos
    {
        get
        {
            return mechInfo.nowPos;
        }
    }

    /// <summary>
    /// 現在角度
    /// </summary>
    public Vector3 NowAngle
    {
        get
        {
            return mechInfo.nowAngle;
        }
    }

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
        if (mechInfo == null)
        {
            return;
        }
        if (mechType == 0)
        {
            // スライダークランク機構
            mechInfo.RenewPos();
        }
        else if (mechType == 1)
        {
            // ゼネバ機構
            mechInfo.RenewPos();
        }
        else if (mechType == 2)
        {
            // レバー機構
            mechInfo.RenewPos();
        }
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        InitializeMechEx();
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
        var floatAngle = 0f;

        // 主軸の動作方向取得
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
                floatAngle = unitSetting.unitObject.transform.localEulerAngles.x;
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
                floatAngle = unitSetting.unitObject.transform.localEulerAngles.y;
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
                floatAngle = unitSetting.unitObject.transform.localEulerAngles.z;
                break;
        }
        initAngle = moveDir.normalized * floatAngle;

        // 作業エリア作成(初期角度分オフセット)
        workSpace = new GameObject("WorkSpace");
        workSpace.transform.parent = unitSetting.unitObject.transform;
        workSpace.transform.localPosition = Vector3.zero;
        workSpace.transform.localEulerAngles = -initAngle;
        workSpace.transform.localScale = new(1, 1, 1);

        // 主軸設定
        var mainAxis = new ExMechAxisInfo
        {
            model = unitSetting.moveObject,
            children = new()
        };
        if (mechType == 0)
        {
            // スライダークランク機構
            mechInfo = new SliderCrankInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange
            };
            // 動作対象(距離で制御する部分)
            mechInfo.sliderAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[1].children)
            {
                mechInfo.sliderAxis.children.Add(child.gameObject);
            }
            // コンロッド(主軸の連結部が原点)
            mechInfo.pntAAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                mechInfo.pntAAxis.children.Add(child.gameObject);
            }
            // LMガイド(動作方向の検出用)
            mechInfo.guideAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[2].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[2].children)
            {
                mechInfo.guideAxis.children.Add(child.gameObject);
            }
            parentModel = mechInfo.sliderAxis.model;
        }
        else if (mechType == 1)
        {
            // ゼネバ機構
            mechInfo = new ExMechGenevaInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange
            };
            // 従動軸
            mechInfo.guideAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                mechInfo.guideAxis.children.Add(child.gameObject);
            }
            // 動作対象(距離で制御する部分)
            mechInfo.sliderAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[1].children)
            {
                mechInfo.sliderAxis.children.Add(child.gameObject);
            }
            parentModel = mechInfo.sliderAxis.model;
        }
        else if (mechType == 2)
        {
            // レバー機構
            mechInfo = new ExMechLeverInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange
            };
            // 動作対象(距離で制御する部分)
            mechInfo.sliderAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                mechInfo.sliderAxis.children.Add(child.gameObject);
            }
            // カムフォロア(主軸の連結部が原点)
            mechInfo.pntAAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[1].children)
            {
                mechInfo.pntAAxis.children.Add(child.gameObject);
            }
            // LMガイド(動作方向の検出用)
            mechInfo.guideAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[2].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[2].children)
            {
                mechInfo.guideAxis.children.Add(child.gameObject);
            }
            parentModel = mechInfo.sliderAxis.model;
        }
    }

    /// <summary>
    /// 機構の初期化
    /// </summary>
    private void InitializeMechEx()
    {
        if (mechType == 0)
        {
            // レバー機構
            mechInfo.Initialize();
        }
        else if (mechType == 1)
        {
            // ゼネバ機構
            mechInfo.Initialize();
        }
        else if (mechType == 2)
        {
            // レバー機構
            mechInfo.Initialize();
        }
    }

    /// <summary>
    /// 目標座標セット
    /// </summary>
    /// <param name="move"></param>
    public void SetExTarget(Vector3 move)
    {
        mechInfo.SetMovePos(move);
    }
}
