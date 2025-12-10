using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst.Intrinsics;
using UnityEngine;
using UnityEngine.SocialPlatforms;

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
        /// <summary>
        /// 逆解計算結果
        /// </summary>
        public struct SolveResult
        {
            public bool valid;        // 実解が存在するか
            public List<float> theta; // 解2
            public float chosen;      // 選ばれた角度 (rad) - prevTheta が与えられた時に選択
            public string message;    // 補助メッセージ
        }

        public bool exModeChange;
        public Vector3 nowPos;
        public Vector3 nowAngle;
        public Vector3 initAngle;
        public AxisInfo mainAxis;
        public AxisInfo pntAAxis;
        public AxisInfo sliderAxis;
        public AxisInfo guideAxis;
        public GameObject workSpace;
        public GameObject guideSpace;
        public GameObject calcSpace;
        public float armL;
        public Vector3 pntAOffset;
        public Vector3 sliderOffset;
        public Vector3 initExPos;
        public Vector3 moveExPos;
        protected Vector3 _mainDir;
        protected Vector3 mainMask;
        protected Vector3 _moveDir;
        protected Vector3 moveMask;
        protected Vector3 _guideDir;
        protected Vector3 guideMask;
        protected Vector3 _sliderDir;
        protected Vector3 sliderMask;
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
        public Vector3 sliderDir
        {
            get
            {
                return _sliderDir;
            }
            set
            {
                sliderMask = new Vector3
                {
                    x = ((value == Vector3.right) || (value == Vector3.left)) ? 0 : 1,
                    y = ((value == Vector3.up) || (value == Vector3.down)) ? 0 : 1,
                    z = ((value == Vector3.forward) || (value == Vector3.back)) ? 0 : 1,
                };
                _sliderDir = value;
            }
        }
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
                // ガイド空間
                guideSpace = new GameObject("GuideSpace");
                guideSpace.transform.parent = workSpace.transform.parent;
                guideSpace.transform.position = mainAxis.model.transform.position;
                guideSpace.transform.eulerAngles = guideAxis.model.transform.eulerAngles;
                guideSpace.transform.localScale = new(1, 1, 1);
                // ガイドの方向
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
        protected virtual Vector3 ForwardKinematics(List<float> angle)
        {
            return Vector3.zero;
        }

        /// <summary>
        /// 逆運動学の解く
        /// </summary>
        protected virtual SolveResult InverseKinematics(Vector3 pos)
        {
            return new();
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
        /// メッシュで一番遠いポイント(Zはない前提)
        /// </summary>
        /// <returns></returns>
        protected Vector3 GetModelFarPoint(GameObject model, ref GameObject obj, Vector3 dir)
        {
            Vector3 point = Vector3.zero;
            var distance = 0f;
            foreach (Transform child in model.GetComponentInChildren<Transform>())
            {
                if (child != model.transform)
                {
                    if (dir.x != dir.y)
                    {
                        if (((dir.x == 0) && (Math.Abs(child.transform.localPosition.x) > 0.001)) || ((dir.y == 0) && (Math.Abs(child.transform.localPosition.y) > 0.001)))
                        {
                            continue;
                        }
                    }
                    var d = Vector3.Distance(Vector3.zero, Vector3.Scale(child.transform.localPosition, dir));
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
        /// ±180°に正規化
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        protected float NormalizeAngle(float angle)
        {
            var tmp = (angle + 360) % 360;
            return  tmp > 180 ? tmp - 360 : tmp;
        }

        /// <summary>
        ///  正規化: -π < angle <= π
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        protected static float NormalizeRad(float a)
        {
            a = (a + Mathf.PI) % (2f * Mathf.PI);
            if (a < 0) a += 2f * Mathf.PI;
            a -= Mathf.PI;
            return a;
        }

        /// <summary>
        /// 移動距離セット
        /// </summary>
        /// <param name="move"></param>
        public void SetMovePos(Vector3 move)
        {
            moveExPos = move;
        }
    }

    /// <summary>
    /// レバー機構
    /// </summary>
    class LeverInfo : ExMechInfo
    {
        public override void Initialize()
        {
            base.Initialize();

            if (exModeChange)
            {
                initExPos = sliderAxis.model.transform.localPosition;
            }

            // カムフォロアの親を主軸に
            pntAAxis.model.transform.parent = mainAxis.model.transform;
        }

        /// <summary>
        /// スライダー位置セット
        /// </summary>
        public override void RenewPos()
        {
            base.RenewPos();
            if (exModeChange)
            {
            }
            else
            {
                sliderAxis.model.transform.position = guideSpace.transform.TransformPoint(sliderOffset + Vector3.Scale(movePos, guideDir));
            }
        }
    }

    /// <summary>
    /// スライダクランク機構
    /// </summary>
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
                return new Vector2(pos.x, pos.y);
            }
        }
        private Vector2 pntBGuidePos;
        private Vector2 pntBGuideOffset;
        Quaternion initRotRotation;
        Quaternion initMainRotation;
        float initMainAngleOffset;
        float initSliderOffset;
        float initSliderZeroX;

        /// <summary>
        /// 初期化処理
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            if (exModeChange)
            {
                initExPos = sliderAxis.model.transform.localPosition;
            }

            // 一旦コンロッドの一番遠いオブジェクト取得
            var farPnt = GetModelFarPoint(pntAAxis.model, ref pntFarObject, new Vector3(1, 1, 0));
            var rodMax = Mathf.Max(Mathf.Abs(farPnt.x), Mathf.Abs(farPnt.y));
            rodDir = new Vector3
            {
                x = rodMax == Mathf.Abs(farPnt.x) ? 1 : 0,
                y = rodMax == Mathf.Abs(farPnt.y) ? 1 : 0,
                z = rodMax == Mathf.Abs(farPnt.z) ? 1 : 0
            };
            // 伸びてる方向がわかったので再度取得
            farPnt = GetModelFarPoint(pntAAxis.model, ref pntFarObject, rodDir);
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
                pntBObject.transform.position = pntFarObject.transform.position;
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
            initRotRotation = Quaternion.Euler(Vector3.Scale(pntAAxis.model.transform.localEulerAngles, rodDir));

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
                }
                else if ((moveDir == Vector3.up) || (moveDir == Vector3.down))
                {
                    // 回転軸がY
                    var yminus = pntA.z < 0 ? 1 : 0;
                    rotation = Quaternion.Euler((xminus != yminus ? 180 : 0) + 90, xminus * 180, 0);
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
                    rotation = Quaternion.Euler(xminus * 180 + 90, xminus != yminus ? 180 : 0, -90);
                }
            }
            else
            {
                // ガイドがZ方向
            }
            var tmp = rotation * pntB;
            yOffset = tmp.y;
            pntBGuideOffset = new Vector2(tmp.x, tmp.y);
            // 計算空間
            calcSpace = new GameObject("CalcSpace");
            calcSpace.transform.parent = workSpace.transform.parent;
            calcSpace.transform.position = mainAxis.model.transform.position;
            calcSpace.transform.localRotation = guideSpace.transform.localRotation * rotation;
            calcSpace.transform.localScale = new(1, 1, 1);
            if (exModeChange)
            {
                // 計算空間へ移動
                mainAxis.model.transform.parent = calcSpace.transform;
                sliderOffset = calcSpace.transform.InverseTransformPoint(sliderAxis.model.transform.position);
            }
            // スライダ初期位置
            initSliderZeroX = Mathf.Sqrt(armM * armM - (armL - yOffset) * (armL - yOffset));
            initSliderOffset = initSliderZeroX - pntBGuideOffset.x;
            // 主軸の初期角度
            var initMainAngle = (Quaternion.Inverse(calcSpace.transform.rotation) * mainAxis.model.transform.rotation).eulerAngles.z;
            // 初回の逆解
            var result = InverseKinematics(new Vector3(pntBGuideOffset.x, yOffset, 0));
            var th = result.theta[0] > result.theta[1] ? result.theta[0] : result.theta[1];
            initMainAngleOffset = th - initMainAngle;
            // 初期値
            nowPos.y = yOffset;
            nowPos.z = 0;
            nowAngle.x = 0;
            nowAngle.y = 0;
        }

        /// <summary>
        /// スライダー位置セット
        /// </summary>
        public override void RenewPos()
        {
            if (exModeChange)
            {
                // スライダ位置計算
                var m = moveExPos.x + moveExPos.y + moveExPos.z;
                var move = new Vector3
                {
                    x = m + initSliderOffset,
                    y = 0,
                    z = 0
                };
                sliderAxis.model.transform.position = calcSpace.transform.TransformPoint(sliderOffset + move);
                // 逆解
                var result = InverseKinematics(new Vector3(initSliderZeroX + m, yOffset, 0));
                if (result.valid)
                {
                    var th = result.theta[0] > result.theta[1] ? result.theta[0] : result.theta[1];
                    mainAxis.model.transform.localEulerAngles = new Vector3(0, 0, th - initMainAngleOffset);
                }
                // スライダ位置
                nowPos.x = m;
            }
            else
            {
                // 2次元で計算
                var y = pntAGuidePos.y - yOffset;
                var x = MathF.Sqrt(armM * armM - y * y);
                pntBGuidePos = new Vector2(pntAGuidePos.x + x, yOffset);
                // スライダの位置
                sliderAxis.model.transform.position = guideSpace.transform.TransformPoint(sliderOffset + Vector3.Scale(movePos, guideDir));
                nowPos.x = x - initSliderOffset;
            }
            // コンロッド端の取得
            var posA = Vector3.Scale(guideSpace.transform.InverseTransformPoint(pntAObject.transform.position), moveMask);
            var posB = Vector3.Scale(guideSpace.transform.InverseTransformPoint(pntBObject.transform.position), moveMask);
            if (modeB)
            {
                pntAAxis.model.transform.position = pntBObject.transform.position;
                // コンロッドの向き
                var rot = Quaternion.FromToRotation(rodDir, posA - posB) * Quaternion.Inverse(initRotRotation);
                pntAAxis.model.transform.localRotation = rot;
            }
            else
            {
                pntAAxis.model.transform.position = pntAObject.transform.position;
                // コンロッドの向き
                var rot = Quaternion.FromToRotation(rodDir, posA - posB) * Quaternion.Inverse(initRotRotation);
                pntAAxis.model.transform.localRotation = rot;
            }
            // 角度と座標取得
            nowAngle.z = NormalizeAngle(90 - (Quaternion.Inverse(calcSpace.transform.rotation) * mainAxis.model.transform.rotation).eulerAngles.z - initMainAngleOffset);
        }

        /// <summary>
        /// 逆解を解く
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        protected override SolveResult InverseKinematics(Vector3 pos)
        {
            SolveResult res = new SolveResult();
            // 基本検査
            if (armL <= 0f || armM <= 0f)
            {
                res.valid = false;
                res.message = "r または l が正でない";
                return res;
            }

            float A = pos.x;
            float B = pos.y;
            float C = (pos.x * pos.x + pos.y * pos.y + armL * armL - armM * armM) / (2f * armL);

            float R = Mathf.Sqrt(A * A + B * B);
            const float EPS = 1e-6f;

            if (R <= EPS)
            {
                // R が0に近い = (x,h) が (0,0) に非常に近い（稀）
                // その場合 AとBが小さく解の安定性が悪い
                res.valid = false;
                res.message = "R が小さすぎて不安定（x,h が原点近傍）";
                return res;
            }

            float ratio = C / R;
            // 数値誤差で +-1 を少し超える可能性があるためクランプ
            if (ratio > 1f + 1e-5f || ratio < -1f - 1e-5f)
            {
                res.valid = false;
                res.message = "物理的に解なし (|C| > R)";
                return res;
            }

            ratio = Mathf.Clamp(ratio, -1f, 1f);

            float alpha = Mathf.Atan2(B, A); // 基準角
            float delta = Mathf.Acos(ratio); // 0..π

            // 二つの解
            float theta1 = alpha + delta;
            float theta2 = alpha - delta;

            // 正規化（-π..π）
            theta1 = NormalizeRad(theta1);
            theta2 = NormalizeRad(theta2);

            res.valid = true;
            res.theta = new();
            res.theta.Add(theta1 * Mathf.Rad2Deg);
            res.theta.Add(theta2 * Mathf.Rad2Deg);
            res.message = "ok";

            // chosen の選択ロジック
            var prevThetaRad = mainAxis.model.transform.localEulerAngles.z - initMainAngleOffset;
            if (!float.IsNaN(prevThetaRad))
            {
                // 正規化して差の小さい方を選ぶ
                float p = NormalizeRad(prevThetaRad);
                float d1 = Mathf.Abs(NormalizeRad(theta1 - p));
                float d2 = Mathf.Abs(NormalizeRad(theta2 - p));
                res.chosen = ((d1 <= d2) ? theta1 : theta2) * Mathf.Rad2Deg;
            }
            else
            {
                // prev が無ければ、物理的に「クランク角がより上死点寄り（小さい絶対値）」などの基準で選ぶ
                // ここでは |theta| が小さい方を選ぶ（用途によって変更可）
                res.chosen = ((Mathf.Abs(theta1) <= Mathf.Abs(theta2)) ? theta1 : theta2) * Mathf.Rad2Deg;
            }
            return res;
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

    /// <summary>
    /// ゼネバ機構
    /// </summary>
    class GenevaInfo : ExMechInfo
    {
        public GameObject pntAObject;
        private Quaternion mainOffsetRot;
        private float initDrivenOffset;
        private Vector3 initPosition;
        private Vector3 initDirvenAng;
        private Vector3 initSliderAng;

        /// <summary>
        /// 初期化処理
        /// </summary>
        public override void Initialize()
        {
            // 制御対象オブジェクトを作成
            pntAObject = new GameObject("PointA");
            pntAObject.transform.parent = mainAxis.model.transform;
            pntAObject.transform.position = sliderAxis.model.transform.position;

            //　主軸
            mainOffsetRot = mainAxis.model.transform.rotation;

            // 計算空間作成
            calcSpace = new GameObject("CalcSpace");
            calcSpace.transform.parent = workSpace.transform.parent;
            calcSpace.transform.position = workSpace.transform.position;
            calcSpace.transform.localRotation = Quaternion.FromToRotation(mainAxis.model.transform.localRotation * Vector3.right, Vector3.Scale((pntAObject.transform.localPosition - mainAxis.model.transform.localPosition), mainMask).normalized) * mainAxis.model.transform.localRotation;
            moveDir = new Vector3(0, 0, 1);

            // 従動軸
            guideDir = guideAxis.model.transform.InverseTransformDirection(calcSpace.transform.forward);
            initDrivenOffset = GetDriveAngle();
            initDirvenAng = guideAxis.model.transform.localEulerAngles;

            // スライダ軸
            sliderDir = sliderAxis.model.transform.InverseTransformDirection(calcSpace.transform.forward);
            initSliderAng = sliderAxis.model.transform.localEulerAngles;

            // 初期位置
            initPosition = calcSpace.transform.InverseTransformPoint(pntAObject.transform.position);
        }

        /// <summary>
        /// スライダー位置セット
        /// </summary>
        public override void RenewPos()
        {
            base.RenewPos();

            // 角度計算
            var th = (GetDriveAngle() - initDrivenOffset);

            // 従動軸の計算
            guideAxis.model.transform.localEulerAngles = th * guideDir + Vector3.Scale(initDirvenAng, guideMask);

            // スライダ軸の計算
            Quaternion deltaMain = mainAxis.model.transform.rotation * Quaternion.Inverse(mainOffsetRot);
            sliderAxis.model.transform.position = pntAObject.transform.position;
            sliderAxis.model.transform.localEulerAngles = th * sliderDir + Vector3.Scale(initSliderAng, sliderMask);

            // 座標更新
            nowPos = Vector3.Scale(calcSpace.transform.InverseTransformPoint(pntAObject.transform.position) - initPosition, new Vector3(-1, 1, 0));
            nowAngle = mainAxis.model.transform.localEulerAngles;
        }

        /// <summary>
        /// 従動軸角度取得
        /// </summary>
        /// <returns></returns>
        private float GetDriveAngle()
        {
            var pntA = Vector3.Scale(calcSpace.transform.InverseTransformPoint(pntAObject.transform.position), moveMask);
            var pntG = pntA - Vector3.Scale(calcSpace.transform.InverseTransformPoint(guideAxis.model.transform.position), moveMask);
            return Mathf.Atan2(pntG.y, pntG.x) * Mathf.Rad2Deg;
        }
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
        var mainAxis = new AxisInfo
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
            parentModel = mechInfo.sliderAxis.model;
        }
        else if (mechType == 1)
        {
            // ゼネバ機構
            mechInfo = new GenevaInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange
            };
            // 従動軸
            mechInfo.guideAxis = new AxisInfo
            {
                model = exMechSetting.datas[0].gameObject,
                children = new()
            };
            foreach (var child in exMechSetting.datas[0].children)
            {
                mechInfo.guideAxis.children.Add(child.gameObject);
            }
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
            parentModel = mechInfo.sliderAxis.model;
        }
        else if (mechType == 2)
        {
            // レバー機構
            mechInfo = new LeverInfo
            {
                workSpace = workSpace,
                mainAxis = mainAxis,
                mainDir = moveDir,
                initAngle = initAngle,
                exModeChange = unitSetting.actionSetting.exModeChange
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
