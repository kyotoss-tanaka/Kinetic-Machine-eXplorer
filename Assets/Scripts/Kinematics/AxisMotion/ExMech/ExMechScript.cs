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
    /// 機構タイプ 0:スライダークランク 1:ゼネバ機構 2:レバー機構 3:並行リンク機構 4:揺動機構
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
        else if (mechType == 3)
        {
            // 並行リンク機構
            mechInfo.RenewPos();
        }
        else if (mechType == 4)
        {
            // 揺動機構
            mechInfo.RenewPos();
        }
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        if (mechInfo == null)
        {
            return;
        }
        // 回転中心が指定されたモデルへピボット空間を挿入する（未指定のモデルは従来どおり原点回転）
        ApplyPivot(mechInfo.mainAxis);
        ApplyPivot(mechInfo.pntAAxis);
        ApplyPivot(mechInfo.sliderAxis);
        ApplyPivot(mechInfo.guideAxis);
        foreach (var axis in mechInfo.axisInfos)
        {
            ApplyPivot(axis);
        }
        InitializeMechEx();
    }

    /// <summary>
    /// 回転中心（種別=回転中心の子モデル）が指定されたモデルに、ピボット空間を挿入する。
    /// 回転中心＝指定モデルの原点（KMXの共通規約。原点が関節/軸中心にあるノードを指定する）。
    /// ピボットは親と同じ向き（ローカル回転=単位）で挿入するため、既存のlocalEulerAngles指定の動作コードがそのまま効く。
    /// </summary>
    private static void ApplyPivot(ExMechAxisInfo axis)
    {
        if ((axis == null) || (axis.model == null) || (axis.pivotSource == null) || (axis.pivot != null))
        {
            return;
        }
        var center = axis.pivotSource.transform.position;
        var pivotGo = new GameObject(axis.model.name + "_Pivot");
        pivotGo.transform.SetParent(axis.model.transform.parent, false);
        // 元モデルと同じローカル姿勢で挿入する（root基準の初期姿勢が従来のモデル基準と一致し、後方互換になる）
        pivotGo.transform.localRotation = axis.model.transform.localRotation;
        pivotGo.transform.localScale = Vector3.one;
        pivotGo.transform.position = center;
        axis.model.transform.SetParent(pivotGo.transform, true);
        axis.pivot = pivotGo;
        Debug.Log($"拡張機構: {axis.model.name} の回転中心を {axis.pivotSource.name} の原点 {center} に設定");
    }

    /// <summary>
    /// 子モデルリストを軸情報へ反映する（種別=回転中心はピボット参照として保持）。
    /// 回転中心(固定)=type2 は中心参照のみで、childrenに含めない（親子付け替え対象外＝据え置き）。
    /// </summary>
    private static void SetAxisChildren(ExMechAxisInfo axis, ExMechModel data)
    {
        foreach (var child in data.children)
        {
            if (child.type == 1)
            {
                axis.pivotSource = child.gameObject;
            }
            else if (child.type == 2)
            {
                axis.pivotSource = child.gameObject;
                continue;
            }
            axis.children.Add(child.gameObject);
        }
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
        // 主軸の子モデル（旧データはmainなし）
        // ※種別=回転中心系はAxisMotionBase.SetExMechSettingがmoveObject自体をピボット化して処理済み
        if (exMechSetting.main != null)
        {
            foreach (var child in exMechSetting.main.children)
            {
                if (child.gameObject == null)
                {
                    continue;
                }
                if (child.type == 2)
                {
                    // 回転中心(固定)は中心参照のみ（据え置き）
                    continue;
                }
                // 通常行・回転中心(追従)行は主軸に追従させる
                child.gameObject.transform.parent = unitSetting.moveObject.transform;
                mainAxis.children.Add(child.gameObject);
            }
        }
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
            SetAxisChildren(mechInfo.sliderAxis, exMechSetting.datas[1]);
            // コンロッド(主軸の連結部が原点)
            mechInfo.pntAAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            SetAxisChildren(mechInfo.pntAAxis, exMechSetting.datas[0]);
            // LMガイド(動作方向の検出用)
            mechInfo.guideAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[2].gameObject,
                children = new()
            };
            SetAxisChildren(mechInfo.guideAxis, exMechSetting.datas[2]);
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
            SetAxisChildren(mechInfo.guideAxis, exMechSetting.datas[0]);
            // 動作対象(距離で制御する部分)
            mechInfo.sliderAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            SetAxisChildren(mechInfo.sliderAxis, exMechSetting.datas[1]);
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
            SetAxisChildren(mechInfo.sliderAxis, exMechSetting.datas[0]);
            // カムフォロア(主軸の連結部が原点)
            mechInfo.pntAAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            SetAxisChildren(mechInfo.pntAAxis, exMechSetting.datas[1]);
            // LMガイド(動作方向の検出用)
            mechInfo.guideAxis = new ExMechAxisInfo
            {
                model = exMechSetting.datas[2].gameObject,
                children = new()
            };
            SetAxisChildren(mechInfo.guideAxis, exMechSetting.datas[2]);
            parentModel = mechInfo.sliderAxis.model;
        }
        else if (mechType == 3)
        {
            // 並行リンク
            var isDouble = exMechSetting.datas[3].gameObject != null &&
                           exMechSetting.datas[4].gameObject != null &&
                           exMechSetting.datas[5].gameObject != null;
            mechInfo = new ExMechParallelLinkInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange,
                isDouble = isDouble
            };
            // アーム
            foreach (var data in exMechSetting.datas)
            {
                var axis = new ExMechAxisInfo
                {
                    model = data.gameObject,
                    children = new()
                };
                SetAxisChildren(axis, data);
                mechInfo.axisInfos.Add(axis);
            }
            if (isDouble)
            {
                parentModel = mechInfo.axisInfos[4].model;
            }
            else
            {
                parentModel = mechInfo.axisInfos[0].model;
            }
        }
        else if (mechType == 4)
        {
            // 揺動機構（直動軸の伸縮で揺動アームを振る）
            mechInfo = new ExMechSwingInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange
            };
            // 直動軸本体 / 揺動アーム / リンク（連結部品）
            var slotNames = new[] { "直動軸本体", "揺動アーム", "リンク" };
            for (var i = 0; i < exMechSetting.datas.Count; i++)
            {
                var data = exMechSetting.datas[i];
                var axis = new ExMechAxisInfo
                {
                    model = data.gameObject,
                    children = new()
                };
                SetAxisChildren(axis, data);
                mechInfo.axisInfos.Add(axis);
                var slot = i < slotNames.Length ? slotNames[i] : $"予備{i}";
                Debug.Log($"揺動機構: [{slot}] モデル={(data.gameObject != null ? data.gameObject.name : "なし")} " +
                    $"回転中心={(axis.pivotSource != null ? axis.pivotSource.name : "なし")} 子={axis.children.Count}");
            }
            Debug.Log($"揺動機構: {unitSetting.name} 主軸(ロッド)={(unitSetting.moveObject != null ? unitSetting.moveObject.name : "null")} " +
                $"拡張機構モード変更={unitSetting.actionSetting.exModeChange}");
            // アームに載る子ユニットはアームへ追従
            parentModel = mechInfo.axisInfos.Count > 1 ? mechInfo.axisInfos[1].model : null;
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
        else if (mechType == 3)
        {
            // 並行リンク機構
            mechInfo.Initialize();
        }
        else if (mechType == 4)
        {
            // 揺動機構
            mechInfo.Initialize();
        }
    }

    /// <summary>
    /// 目標座標セット
    /// </summary>
    /// <param name="move"></param>
    public void SetExTarget(Vector3 move)
    {
        if (mechInfo == null)
        {
            // 機構情報が未初期化（設定不備・初期化失敗）の場合は無視する
            // （ここでNREを出すとロードコルーチンが死んでF5が効かなくなる）
            return;
        }
        if (mechType == 4)
        {
            // 揺動機構: ユニットの動作方向(±)を駆動符号として反映する。
            // 駆動値を作るMotionInternal側のmoveDirは軸のみで±を持たないため、ここで符号を掛ける
            // （moveDirはこのクラスで動作設定のaxis+dirから作った±付きの単位軸ベクトル）
            var sign = (moveDir.x + moveDir.y + moveDir.z) < 0f ? -1f : 1f;
            move *= sign;
        }
        mechInfo.SetMovePos(move);
    }
}
