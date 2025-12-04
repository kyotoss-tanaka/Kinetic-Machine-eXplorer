using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using static GlobalScript;
using static OVRPlugin;
using static UnityEngine.Rendering.DebugUI.Table;

public class ExMechScript : UseTagBaseScript
{
    [Serializable]
    class AxisInfo
    {
        [SerializeField]
        public GameObject model;
        [SerializeField]
        public List<GameObject> children;
        /// <summary>
        /// 親をセットする
        /// </summary>
        /// <param name="parent"></param>
        public void SetParent(GameObject parent)
        {
            model.transform.parent = parent.transform;
            foreach (var child in children)
            {
                child.transform.parent = parent.transform;
            }
        }
    }

    class ExMechInfo
    {
        public Vector3 nowPos;
        public Vector3 nowAngle;
        public Vector3 initAngle;
        public AxisInfo mainAxis;
        public AxisInfo pntAAxis;
        public AxisInfo sliderAxis;
        public AxisInfo guideAxis;
        public GameObject workSpace;
        public GameObject guideSpace;
        public float armL;
        public Vector3 pntAOffset;
        public Vector3 sliderOffset;
        public Vector3 mainDir
        {
            get
            {
                return _mainDir;
            }
            set
            {
                mainMask = new Vector3
                {
                    x = ((value == Vector3.right) || (value == Vector3.left)) ? 0 : 1,
                    y = ((value == Vector3.up) || (value == Vector3.down)) ? 0 : 1,
                    z = ((value == Vector3.forward) || (value == Vector3.back)) ? 0 : 1,
                };
                _mainDir = value;
            }
        }
        public Vector3 moveDir
        {
            get
            {
                return _moveDir;
            }
            set
            {
                moveMask = new Vector3
                {
                    x = ((value == Vector3.right) || (value == Vector3.left)) ? 0 : 1,
                    y = ((value == Vector3.up) || (value == Vector3.down)) ? 0 : 1,
                    z = ((value == Vector3.forward) || (value == Vector3.back)) ? 0 : 1,
                };
                _moveDir = value;
            }
        }
        public Vector3 guideDir
        {
            get
            {
                return _guideDir;
            }
            set
            {
                guideMask = new Vector3
                {
                    x = ((value == Vector3.right) || (value == Vector3.left)) ? 0 : 1,
                    y = ((value == Vector3.up) || (value == Vector3.down)) ? 0 : 1,
                    z = ((value == Vector3.forward) || (value == Vector3.back)) ? 0 : 1,
                };
                _guideDir = value;
            }
        }
        protected Vector3 _mainDir;
        protected Vector3 mainMask;
        protected Vector3 _moveDir;
        protected Vector3 moveMask;
        protected Vector3 _guideDir;
        protected Vector3 guideMask;
        public Vector3 pntAPos
        {
            get
            {
                if (guideSpace != null)
                {
                    return guideSpace.transform.InverseTransformPoint(pntAAxis.model.transform.position);
                }
                else
                {
                    return workSpace.transform.InverseTransformPoint(pntAAxis.model.transform.position);
                }
            }
        }
        public Vector3 sliderPos
        {
            get
            {
                if (guideSpace != null)
                {
                    return guideSpace.transform.InverseTransformPoint(sliderAxis.model.transform.position);
                }
                else
                {
                    return workSpace.transform.InverseTransformPoint(sliderAxis.model.transform.position);
                }
            }
        }
        public virtual Vector3 movePos
        {
            get
            {
                return pntAPos - pntAOffset;
            }
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public virtual void Initialize()
        {
            // LMガイドの座標系をセット
            if (guideAxis == null)
            {
                moveDir = mainDir;
            }
            else
            {
                guideSpace = new GameObject("GuideSpace");
                guideSpace.transform.parent = workSpace.transform.parent;
                guideSpace.transform.position = mainAxis.model.transform.position;
                guideSpace.transform.eulerAngles = guideAxis.model.transform.eulerAngles;
                guideSpace.transform.localScale = new(1, 1, 1);

                guideDir = guideSpace.transform.InverseTransformVector(GetMechDir(guideAxis.model));
                moveDir = guideAxis.model.transform.InverseTransformVector(mainAxis.model.transform.TransformVector(mainDir));
            }
            pntAOffset = pntAPos;
            sliderOffset = sliderPos;
            armL = Vector3.Distance(Vector3.zero, Vector3.Scale(pntAOffset, moveMask));
        }

        /// <summary>
        /// 位置更新処理
        /// </summary>
        public virtual void RenewPos()
        {
            // メイン角度算出
            Quaternion worldRot = mainAxis.model.transform.rotation;
            Quaternion workspaceLocalRot = Quaternion.Inverse(workSpace.transform.rotation) * worldRot;
            nowAngle = Vector3.Scale(workspaceLocalRot.eulerAngles, moveDir);
            nowAngle.x = NormalizeAngle(nowAngle.x - 90) * Math.Abs(moveDir.x);
            nowAngle.y = NormalizeAngle(nowAngle.y - 90) * Math.Abs(moveDir.y);
            nowAngle.z = NormalizeAngle(nowAngle.z - 90) * Math.Abs(moveDir.z);
        }

        /// <summary>
        /// 順運動学の解く
        /// </summary>
        protected virtual void ForwardKinematics()
        {
        }

        /// <summary>
        /// 逆運動学の解く
        /// </summary>
        protected virtual void InverseKinematics()
        {
        }

        /// <summary>
        /// モデルの方向を取得(ワールド座標)
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        protected Vector3 GetMechDir(GameObject model)
        {
            var dir = new Vector3();
            Vector3 maxSize = new();
            float maxLength = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var bounds = mesh.bounds; // ローカル座標でのバウンディングボックス

                Vector3 size = bounds.size; // x, y, zそれぞれの長さ

                // 最長方向を求める
                float max = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (maxLength < max)
                {
                    maxLength = max;
                    maxSize = size;
                }
            }
            if (maxSize.x == maxLength)
            {
                dir = Vector3.right;
            }
            else if (maxSize.y == maxLength)
            {
                dir = Vector3.up;
            }
            else
            {
                dir = Vector3.forward;
            }
            return model.transform.TransformVector(dir);
        }

        /// <summary>
        /// メッシュで一番遠いポイント
        /// </summary>
        /// <returns></returns>
        protected Vector3 GetModelFarPoint(GameObject model, ref GameObject obj)
        {
            Vector3 point = Vector3.zero;
            var distance = 0f;
            foreach (Transform child in model.GetComponentInChildren<Transform>())
            {
                if (child != model.transform)
                {
                    var d = Vector3.Distance(Vector3.zero, child.transform.localPosition);
                    if (distance < d)
                    {
                        distance = d;
                        point = child.transform.localPosition;
                        obj = child.gameObject;
                    }
                }
            }
            return point;
        }

        /// <summary>
        /// メッシュで一番遠いポイント
        /// </summary>
        /// <returns></returns>
        protected Vector3 GetModelNearPoint(GameObject model, ref GameObject obj)
        {
            Vector3 point = Vector3.zero;
            var distance = 1000f;
            foreach (Transform child in model.GetComponentInChildren<Transform>())
            {
                if (child != model.transform)
                {
                    var d = Vector3.Distance(Vector3.zero, child.transform.localPosition);
                    if (distance > d)
                    {
                        distance = d;
                        point = child.transform.localPosition;
                        obj = child.gameObject;
                    }
                }
            }
            return point;
        }

        /// <summary>
        /// 0-360°に正規化
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        protected float NormalizeAngle(float angle)
        {
            return (angle + 360) % 360;
        }
    }

    class LeverInfo : ExMechInfo
    {
        public override void Initialize()
        {
            base.Initialize();

            // カムフォロアの親を主軸に
            pntAAxis.model.transform.parent = mainAxis.model.transform;
        }

        /// <summary>
        /// スライダー位置セット
        /// </summary>
        public override void RenewPos()
        {
            base.RenewPos();
            sliderAxis.model.transform.position = guideSpace.transform.TransformPoint(sliderOffset + Vector3.Scale(movePos, guideDir));
        }
    }

    class SliderCrankInfo : ExMechInfo
    {
        public bool modeB = false;
        public float armM;
        private Vector3 rodDir;
        public GameObject pntAObject;
        public GameObject pntBObject;
        public GameObject pntFarObject;
        public Vector3 pntBOffset;
        private Quaternion rotation = new();
        private float yOffset;
        public override Vector3 movePos
        {
            get
            {
                return Quaternion.Inverse(rotation) * (new Vector3((pntBGuidePos - pntBGuideOffset).x, 0, 0));
            }
        }
        private Vector2 pntAGuidePos
        {
            get
            {
                var pos = rotation * guideSpace.transform.InverseTransformPoint(pntAObject.transform.position);
                if ((guideDir == Vector3.right) || (guideDir == Vector3.left))
                {
                    if ((moveDir == Vector3.forward) || (moveDir == Vector3.back))
                    {
                        // 回転軸がZ
                        return new Vector2(pos.x, pos.y);
                    }
                    else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
                    {
                        // 回転軸がY
                        return new Vector2(pos.x, pos.z);
                    }
                }
                else if ((guideDir == Vector3.up) || (guideDir == Vector3.down))
                {
                    // ガイドがY方向
                    if ((moveDir == Vector3.forward) || (moveDir == Vector3.back))
                    {
                        // 回転軸がZ
                        return new Vector2(pos.x, pos.y);

                    }
                    else if ((moveDir == Vector3.right) || (moveDir == Vector3.left))
                    {
                        // 回転軸がX
                        return new Vector2(pos.x, pos.z);
                    }
                }
                else
                {
                    // ガイドがZ方向
                }
                return new();
            }
        }
        private Vector2 pntBGuidePos;
        private Vector2 pntBGuideOffset;
        Quaternion initRotation;

        /// <summary>
        /// 初期化処理
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            // コンロッドの一番遠いオブジェクト取得
            var farPnt = GetModelFarPoint(pntAAxis.model, ref pntFarObject);
            var pos = pntAAxis.model.transform.TransformPoint(farPnt);
            pntBOffset = guideSpace.transform.InverseTransformPoint(pos);

            // コンロッドの主軸側判定
            var tmpA = Vector3.Scale(mainAxis.model.transform.InverseTransformPoint(pntAAxis.model.transform.position), mainMask);
            var tmpB = Vector3.Scale(mainAxis.model.transform.InverseTransformPoint(pntFarObject.transform.position), mainMask);

            // 制御対象オブジェクトを作成
            pntAObject = new GameObject("PointA");
            pntBObject = new GameObject("PointB");

            // コンロッドの根元がどちら側かチェック(主軸の軸上にいるか)
            if (CheckRod(tmpA, tmpB))
            {
                // AとB入れ替え
                armL = Vector3.Distance(Vector3.zero, tmpB);
                modeB = true;
                pntBOffset = pntAOffset;
                pntAOffset = guideSpace.transform.InverseTransformPoint(pos);
                pntAObject.transform.position = pntFarObject.transform.position;
                pntAObject.transform.eulerAngles = pntFarObject.transform.eulerAngles;
                pntBObject.transform.position = pntAAxis.model.transform.position;
            }
            else
            {
                pntAObject.transform.position = pntAAxis.model.transform.position;
                pntAObject.transform.eulerAngles = pntAAxis.model.transform.eulerAngles;
                pntBObject.transform.position = sliderAxis.model.transform.position;
            }
            pntAObject.transform.parent = mainAxis.model.transform;
            pntBObject.transform.parent = sliderAxis.model.transform;

            // コンロッドの親設定
            pntAAxis.SetParent(guideSpace);

            // コンロッドの方向取得
            var conA = pntAAxis.model.transform.InverseTransformPoint(guideSpace.transform.TransformPoint(Vector3.Scale(pntAOffset, moveMask)));
            var conB = pntAAxis.model.transform.InverseTransformPoint(guideSpace.transform.TransformPoint(Vector3.Scale(pntBOffset, moveMask)));
            var conAB = conB - conA;
            armM = Mathf.Max(Mathf.Abs(conAB.x), Mathf.Max(Mathf.Abs(conAB.y), Mathf.Abs(conAB.z)));
            if (armM == conAB.x)
            {
                rodDir = conAB.x < 0 ? Vector3.left : Vector3.right;
            }
            else if (armM == conAB.y)
            {
                rodDir = conAB.y < 0 ? Vector3.up : Vector3.down;
            }
            else
            {
                rodDir = conAB.z < 0 ? Vector3.forward : Vector3.back;
            }
            // 初期姿勢
            initRotation = Quaternion.Euler(Vector3.Scale(pntAAxis.model.transform.localEulerAngles, rodDir));

            // ガイド基準に変更
            var pntA = Vector3.Scale(pntAOffset, moveMask);
            var pntB = Vector3.Scale(pntBOffset, moveMask);

            // ガイドの方向別処理
            if ((guideDir == Vector3.right) || (guideDir == Vector3.left))
            {
                // ガイドがX方向
                // マイナス方向判定
                var xminus = (pntB - pntA).x < 0 ? 1 : 0;
                if ((moveDir == Vector3.forward) || (moveDir == Vector3.back))
                {
                    // 回転軸がZ
                    var yminus = pntA.y < 0 ? 1 : 0;
                    rotation = Quaternion.Euler(xminus != yminus ? 180 : 0, 0, xminus * 180);
                    yOffset = (rotation * pntB).y;
                    var tmp = rotation * pntB;
                    pntBGuideOffset = new Vector2(tmp.x, tmp.y);
                }
                else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
                {
                    // 回転軸がY
                    var yminus = pntA.z < 0 ? 1 : 0;
                    rotation = Quaternion.Euler(xminus != yminus ? 180 : 0, xminus * 180, 0);
                    yOffset = (rotation * pntB).z;
                    var tmp = rotation * pntB;
                    pntBGuideOffset = new Vector2(tmp.x, tmp.z);
                }
            }
            else if ((guideDir == Vector3.up) || (guideDir == Vector3.down))
            {
                // ガイドがY方向
                var xminus = (pntB - pntA).y < 0 ? 1 : 0;
                if ((moveDir == Vector3.forward) || (moveDir == Vector3.back))
                {
                    // 回転軸がZ
                    var yminus = pntA.x < 0 ? 1 : 0;

                }
                else if ((moveDir == Vector3.right) || (moveDir == Vector3.left))
                {
                    // 回転軸がX
                    var yminus = pntA.z < 0 ? 1 : 0;
                    rotation = Quaternion.Euler(xminus * 180, xminus != yminus ? 180 : 0, -90);
                    yOffset = (rotation * pntB).z;
                    var tmp = rotation * pntB;
                    pntBGuideOffset = new Vector2(tmp.x, tmp.z);
                }
            }
            else
            {
                // ガイドがZ方向
            }
        }

        /// <summary>
        /// スライダー位置セット
        /// </summary>
        public override void RenewPos()
        {
            base.RenewPos();

            // 2次元で計算
            var y = pntAGuidePos.y - yOffset;
            var x = MathF.Sqrt(armM * armM - y * y);
            pntBGuidePos = new Vector2(pntAGuidePos.x + x, yOffset);
            var thA = new Vector3(Mathf.Atan2(pntAGuidePos.y, pntAGuidePos.x) * Mathf.Rad2Deg, 0, 0);
            var thB = new Vector3(0, 0, Mathf.Atan2(pntAGuidePos.y - pntBGuidePos.y, pntBGuidePos.x - pntAGuidePos.x) * Mathf.Rad2Deg);
            // スライダの位置
            sliderAxis.model.transform.position = guideSpace.transform.TransformPoint(sliderOffset + Vector3.Scale(movePos, guideDir));
            // コンロッド端の取得
            var posA = Vector3.Scale(guideSpace.transform.InverseTransformPoint(pntAObject.transform.position), moveMask);
            var posB = Vector3.Scale(guideSpace.transform.InverseTransformPoint(pntBObject.transform.position), moveMask);
            if (modeB)
            {
                pntAAxis.model.transform.position = pntBObject.transform.position;
                // コンロッドの向き
                var rot = Quaternion.FromToRotation(rodDir, posA - posB) * Quaternion.Inverse(initRotation);
                pntAAxis.model.transform.localRotation = rot;
            }
            else
            {
                pntAAxis.model.transform.position = pntAObject.transform.position;
                // コンロッドの向き
                var rot = Quaternion.FromToRotation(rodDir, posA - posB) * Quaternion.Inverse(initRotation);
                pntAAxis.model.transform.localRotation = rot;
            }
            ForwardKinematics();
        }

        /// <summary>
        /// 順運動学を解く
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        protected override void ForwardKinematics()
        {

        }

        /// <summary>
        /// 逆運動学を解く
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        protected override void InverseKinematics()
        {
        }

        /// <summary>
        /// 根元がBかチェック
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private bool CheckRod(Vector3 a, Vector3 b)
        {
            var dataA = new List<float>
            {
                Math.Abs(a.x + a.y),
                Math.Abs(a.x + a.z),
                Math.Abs(a.y + a.z)
            };
            var dataB = new List<float>
            {
                Math.Abs(b.x + b.y),
                Math.Abs(b.x + b.z),
                Math.Abs(b.y + b.z)
            };
            var minA = dataA.Min();
            var minB = dataB.Min();
            return minA > minB;
        }
    }

    class GenevaInfo : ExMechInfo
    {
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
    /// 作業オブジェクト
    /// </summary>
    GameObject workSpace;

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
    Vector3 drivenPrv;

    /// <summary>
    /// 前回の姿勢保持
    /// </summary>
    List<Vector3> intermediatePrv;

    /// <summary>
    /// 従動軸のベース空間
    /// </summary>
    GameObject drivenBase;

    /// <summary>
    /// 中間軸のベース空間
    /// </summary>
    List<GameObject> intermediateBase;

    /// <summary>
    /// ロッドの方向
    /// </summary>
    Vector3 rodDir;

    /// <summary>
    /// LMの方向
    /// </summary>
    Vector3 lmDir;

    /// <summary>
    /// ロッド長
    /// </summary>
    Vector3 rodLength;

    /// <summary>
    /// 初期角度
    /// </summary>
    private Vector3 initAngle = Vector3.zero;

    /// <summary>
    /// 機構情報
    /// </summary>
    private ExMechInfo mechInfo;

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
            // 駆動軸の座標系に変換
            mechInfo.RenewPos();
        }
        else if (mechType == 1)
        {
            // 駆動軸の座標系に変換
            var mainPos = Vector3.zero;
            var sliderPos = mainAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);

            // シャフト位置計算
            Vector3 point = armDir * armL + Vector3.Scale(sliderPos, maskDir2);

            // モデル1のローカル座標をワールド座標に変換
            intermediateAxis[0].model.transform.position = mainAxis.model.transform.TransformPoint(point);

            // ゼネバ機構
            // 従動軸角度設定
            sliderPos = drivenBase.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
            var mSliderPos = Vector3.Scale(sliderPos, maskDir1);
            var angle = GetAngle(Vector3.zero, mSliderPos) - drivenOffset;
            var eulerAngle = Vector3.Scale(new Vector3(angle, angle, angle), drivenBase.transform.localScale);
            drivenAxis.model.transform.localEulerAngles = Vector3.Scale(eulerAngle, maskDir2) + Vector3.Scale(drivenPrv, maskDir1);

            // 中間軸角度設定
            var drivenPos = intermediateBase[0].transform.InverseTransformPoint(drivenAxis.model.transform.position);
            var mDrivenPos = Vector3.Scale(sliderPos, maskDir1);
            angle = GetAngle(Vector3.zero, mDrivenPos) - intermediateOffset[0];
            eulerAngle = Vector3.Scale(new Vector3(angle, angle, angle), intermediateBase[0].transform.localScale);
            intermediateAxis[0].model.transform.localEulerAngles = Vector3.Scale(eulerAngle, maskDir2) + Vector3.Scale(intermediateBase[0].transform.localEulerAngles, maskDir1);
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
        // 方向マスク取得
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
        InitializeMechEx();
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

        // データ初期化
        intermediateAxis = new();
        // 主軸設定
        mainAxis = new AxisInfo
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
                initAngle = initAngle
            };
            // 動作対象(距離で制御する部分)
            mechInfo.sliderAxis = new AxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[1].children)
            {
                mechInfo.sliderAxis.children.Add(child.gameObject);
            }
            // コンロッド(主軸の連結部が原点)
            mechInfo.pntAAxis = new AxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                mechInfo.pntAAxis.children.Add(child.gameObject);
            }
            // LMガイド(動作方向の検出用)
            mechInfo.guideAxis = new AxisInfo
            {
                model = exMechSetting.datas[2].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[2].children)
            {
                mechInfo.guideAxis.children.Add(child.gameObject);
            }
        }
        else if (mechType == 1)
        {
            // ゼネバ機構
            mechInfo = new GenevaInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle
            };
            // 従動軸
            drivenAxis = new AxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                drivenAxis.children.Add(child.gameObject);
            }
            // スライダー軸(主軸の連結部が原点)
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
        else if (mechType == 2)
        {
            // レバー機構
            mechInfo = new LeverInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle
            };
            // 動作対象(距離で制御する部分)
            mechInfo.sliderAxis = new AxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                mechInfo.sliderAxis.children.Add(child.gameObject);
            }
            // カムフォロア(主軸の連結部が原点)
            mechInfo.pntAAxis = new AxisInfo
            {
                model = exMechSetting.datas[1].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[1].children)
            {
                mechInfo.pntAAxis.children.Add(child.gameObject);
            }
            // LMガイド(動作方向の検出用)
            mechInfo.guideAxis = new AxisInfo
            {
                model = exMechSetting.datas[2].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[2].children)
            {
                mechInfo.guideAxis.children.Add(child.gameObject);
            }
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
            var mainPos = Vector3.zero;
            var sliderPos = mainAxis.model.transform.InverseTransformPoint(intermediateAxis[0].model.transform.position);
            var mMainPos = Vector3.Scale(mainPos, maskDir1);
            var mSliderPos = Vector3.Scale(sliderPos, maskDir1);

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
            drivenPrv = drivenAxis.model.transform.localEulerAngles;
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
            intermediatePrv = new();
            intermediatePrv.Add(intermediateAxis[0].model.transform.localEulerAngles);
            intermediateBase = new();
            intermediateBase.Add(new GameObject("intermediateBase"));
            intermediateBase[0].transform.parent = intermediateAxis[0].model.transform.parent;
            intermediateBase[0].transform.localPosition = intermediateAxis[0].model.transform.localPosition;
            intermediateBase[0].transform.localEulerAngles = intermediateAxis[0].model.transform.localEulerAngles;
            intermediateBase[0].transform.localScale = intermediateAxis[0].model.transform.localScale;
        }
        else if (mechType == 2)
        {
            // レバー機構
            mechInfo.Initialize();
        }
    }
}
