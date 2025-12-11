using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst.Intrinsics;
using UnityEngine;
using UnityEngine.SocialPlatforms;

public class ExMechInfo
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
    public ExMechAxisInfo mainAxis;
    public ExMechAxisInfo pntAAxis;
    public ExMechAxisInfo sliderAxis;
    public ExMechAxisInfo guideAxis;
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
        return tmp > 180 ? tmp - 360 : tmp;
    }

    /// <summary>
    ///  正規化: -π < angle <= π
    /// </summary>
    /// <param name="a"></param>
    /// <returns></returns>
    protected float NormalizeRad(float a)
    {
        a = (a + Mathf.PI) % (2f * Mathf.PI);
        if (a < 0) a += 2f * Mathf.PI;
        a -= Mathf.PI;
        return a;
    }

    /// <summary>
    /// 動作モード変更時の移動距離セット
    /// </summary>
    /// <param name="move"></param>
    public void SetMovePos(Vector3 move)
    {
        moveExPos = move;
    }
}