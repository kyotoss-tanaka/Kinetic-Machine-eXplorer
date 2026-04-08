using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class MotionLinear : AxisMotionBase
{
    #region クラス定義
    /// <summary>
    /// ムーバー情報
    /// </summary>
    protected class MoverInfo
    {
        public GameObject obj;
        public int pointno = -1;
        public float pos = 0;
    }
    #endregion クラス定義

    #region メンバー定義
    /// <summary>
    /// バケット情報
    /// </summary>
    protected List<MoverInfo> movers = new List<MoverInfo>();

    /// <summary>
    /// 計算長
    /// </summary>
    private float calcLength;

    /// <summary>
    /// 周長
    /// </summary>
    private float totalLength;

    /// <summary>
    /// 直線距離
    /// </summary>
    private float straightLength;

    /// <summary>
    /// カーブ距離(理論上)
    /// </summary>
    private float curveLength;

    /// <summary>
    /// カーブ距離
    /// </summary>
    private float calcCurveLength;

    /// <summary>
    /// ムーバーピッチ
    /// </summary>
    private float moverPitch;

    /// <summary>
    /// 初期位置オフセット
    /// </summary>
    private float moverOffsetPos;

    /// <summary>
    /// 高さ方向オフセット
    /// </summary>
    private float moverOffsetH;

    /// <summary>
    /// 初期角度オフセット
    /// </summary>
    private Vector3 moverOffsetAng;

    /// <summary>
    /// リニアの動作方向
    /// </summary>
    private bool isLinearRvs;

    /// <summary>
    /// モデル中心
    /// </summary>
    private Vector3 center;
    #endregion メンバー定義

    #region 関数定義
    /// <summary>
    /// モデル再構築
    /// </summary>
    protected override void PreModelRestruct()
    {
        base.PreModelRestruct();

        if (linearSetting != null)
        {
            // リニアパス作成
            CreateLinearPathPoints();

            // ムーバー作成
            CreateMoverObject();
        }
    }

    /// <summary>
    /// リニアループのポイント作成
    /// </summary>
    private void CreateLinearPathPoints()
    {
        var meshFilters = new List<MeshFilter>();
        meshFilters.AddRange(moveObject.GetComponentsInChildren<MeshFilter>());
        // 真下の子供だけ取得
        foreach (var child in unitSetting.childrenObject.FindAll(d => d.GetComponent<AxisMotionBase>() == null))
        {
            meshFilters.AddRange(child.GetComponentsInChildren<MeshFilter>());
        }
        if (meshFilters.Count > 0)
        {
            // 全頂点情報取得
            var allVerts = new List<VerticeInfo>();
            foreach (var mf in meshFilters)
            {
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var normals = mesh.normals;
                for (int i = 0; i < verts.Length; i++)
                {
                    allVerts.Add(new VerticeInfo
                    {
                        vertice = moveObject.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[i])),
                        normal = moveObject.transform.InverseTransformPoint(mf.transform.TransformPoint(normals[i]))
                    });
                }
            }
            // 許容誤差（必要に応じて調整）
            float tolerance = 0.0000001f;
            // 同一点を削除(1μm以下は同一の点とする)
            allVerts = allVerts.GroupBy(v => new
            {
                x = Mathf.Round(v.vertice.x / tolerance),
                y = Mathf.Round(v.vertice.y / tolerance),
                z = Mathf.Round(v.vertice.z / tolerance)
            }).Select(g => g.First()).ToList();
            var m = new List<float>{
                        allVerts.Max(d => d.vertice.x) - allVerts.Min(d => d.vertice.x),
                        allVerts.Max(d => d.vertice.y) - allVerts.Min(d => d.vertice.y),
                        allVerts.Max(d => d.vertice.z) - allVerts.Min(d => d.vertice.z)
                    };
            // 奥行方向算出
            var min = m.Min() == m[0] ? 0 : m.Min() == m[1] ? 1 : 2;
            // 流れ方向算出
            var max = m.Max() == m[0] ? 0 : m.Max() == m[1] ? 1 : 2;
            // 高さ方向算出
            var mid = 3 - max - min;
            // 疑似中心取得
            center = new Vector3(
                min == 0 ? 0 : allVerts.Average(d => d.vertice.x),
                min == 1 ? 0 : allVerts.Average(d => d.vertice.y),
                min == 2 ? 0 : allVerts.Average(d => d.vertice.z)
            );
            // 位置遠い点を取得
            var farthest = allVerts.OrderByDescending(v => Vector3.Distance(
                new Vector3(
                    min == 0 ? 0 : v.vertice.x,
                    min == 1 ? 0 : v.vertice.y,
                    min == 2 ? 0 : v.vertice.z),
                center)).First();
            // 一番遠い点と同じ外周を取得
            var outlines = allVerts.FindAll(d =>
                min == 0 ? Math.Abs(d.vertice.x - farthest.vertice.x) < tolerance :
                min == 1 ? Math.Abs(d.vertice.y - farthest.vertice.y) < tolerance :
                Math.Abs(d.vertice.z - farthest.vertice.z) < tolerance);
            // 高さ方向の最大最小取得
            var hMin = outlines.Min(d => mid == 0 ? d.vertice.x : mid == 1 ? d.vertice.y : d.vertice.z);
            var hMax = outlines.Max(d => mid == 0 ? d.vertice.x : mid == 1 ? d.vertice.y : d.vertice.z);
            var hMinVer = outlines.FindAll(d =>
                mid == 0 ? Math.Abs(d.vertice.x - hMin) < tolerance :
                mid == 1 ? Math.Abs(d.vertice.y - hMin) < tolerance :
                Math.Abs(d.vertice.z - hMin) < tolerance);
            var hMaxVer = outlines.FindAll(d => 
                mid == 0 ? Math.Abs(d.vertice.x - hMax) < tolerance : 
                mid == 1 ? Math.Abs(d.vertice.y - hMax) < tolerance :
                Math.Abs(d.vertice.z - hMax) < tolerance);
            // 流れ方向の最大最小取得
            var lMin = hMinVer.Min(d => max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z);
            var lMax = hMinVer.Max(d => max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z);
            straightLength = lMax - lMin;
            // 直線部を削除
            outlines = outlines.FindAll(d =>
                max == 0 ? (d.vertice.x >= lMax) || (d.vertice.x <= lMin) :
                max == 1 ? (d.vertice.y >= lMax) || (d.vertice.y <= lMin) :
                (d.vertice.z >= lMax) || (d.vertice.z <= lMin));
            hMinVer = hMinVer.FindAll(d =>
                max == 0 ? (d.vertice.x >= lMax) || (d.vertice.x <= lMin) :
                max == 1 ? (d.vertice.y >= lMax) || (d.vertice.y <= lMin) :
                (d.vertice.z >= lMax) || (d.vertice.z <= lMin)).OrderBy(d =>
                max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z).ToList();
            hMaxVer = hMaxVer.FindAll(d =>
                max == 0 ? (d.vertice.x >= lMax) || (d.vertice.x <= lMin) :
                max == 1 ? (d.vertice.y >= lMax) || (d.vertice.y <= lMin) :
                (d.vertice.z >= lMax) || (d.vertice.z <= lMin)).OrderBy(d =>
                max == 0 ? d.vertice.x : max == 1 ? d.vertice.y : d.vertice.z).ToList();
            // 中心算出
            center = new Vector3(
                max == 0 ? (lMax - lMin) / 2 + lMin : mid == 0 ? (hMax - hMin) / 2 + hMin : farthest.vertice.x,
                max == 1 ? (lMax - lMin) / 2 + lMin : mid == 1 ? (hMax - hMin) / 2 + hMin : farthest.vertice.y,
                max == 2 ? (lMax - lMin) / 2 + lMin : mid == 2 ? (hMax - hMin) / 2 + hMin : farthest.vertice.z
            );
            // 凸凹を無くす
            outlines = ConvexHull(outlines, max, mid);
            // 回転方向にソート
            outlines = isLinearRvs ?
                outlines.OrderBy(d =>
                {
                    double angle = Math.Atan2(
                        mid == 0 ? d.vertice.x - center.x : mid == 1 ? d.vertice.y - center.y : d.vertice.z - center.z,
                        max == 0 ? d.vertice.x - center.x : max == 1 ? d.vertice.y - center.y : d.vertice.z - center.z
                    );
                    if (angle < 0) angle += Math.PI * 2; // 0〜2πに正規化
                    return angle;
                }).ToList() :
                outlines.OrderByDescending(d =>
                {
                    double angle = Math.Atan2(
                        mid == 0 ? d.vertice.x - center.x : mid == 1 ? d.vertice.y - center.y : d.vertice.z - center.z,
                        max == 0 ? d.vertice.x - center.x : max == 1 ? d.vertice.y - center.y : d.vertice.z - center.z
                    );
                    if (angle < 0) angle += Math.PI * 2; // 0〜2πに正規化
                    return angle;
                }).ToList();
            var index = outlines.IndexOf(hMaxVer[0]);
            outlines = outlines.Skip(index).Concat(outlines.Take(index)).ToList();
            // 奥行方向をムーバーオブジェクトと合わせる
            linearSetting.gameObject.transform.parent = moveObject.transform;
            moverOffsetH = mid == 0 ? linearSetting.gameObject.transform.localPosition.x : mid == 1 ? linearSetting.gameObject.transform.localPosition.y : linearSetting.gameObject.transform.localPosition.z;
            var points = outlines.Select(d =>
            {
                var pos = new Vector3
                {
                    x = min == 0 ? linearSetting.gameObject.transform.localPosition.x : d.vertice.x,
                    y = min == 1 ? linearSetting.gameObject.transform.localPosition.y : d.vertice.y,
                    z = min == 2 ? linearSetting.gameObject.transform.localPosition.z : d.vertice.z
                };
                return pos;
            }
            ).ToList();
            //loopPathPoints.AddRange(points);
            loopPathPoints.AddRange(OffsetPath(points, moverOffsetH));
            loopPathPoints.Add(loopPathPoints[0]);
            // パスの総距離を計算
            calcLength = 0f;
            for (int i = 0; i < loopPathPoints.Count - 1; i++)
            {
                calcLength += Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);
            }
            // カーブ距離算出
            calcCurveLength = calcLength / 2 - straightLength;
        }
    }

    /// <summary>
    /// 平面上で凸包を計算
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    private List<VerticeInfo> ConvexHull(List<VerticeInfo> points, int max, int mid)
    {
        // X座標でソート
        var sorted = points
            .OrderBy(p => max == 0 ? p.vertice.x : max == 1 ? p.vertice.y : p.vertice.z)
            .ThenBy(p => mid == 0 ? p.vertice.x : mid == 1 ? p.vertice.y : p.vertice.z)
            .ToList();

        var hull = new List<VerticeInfo>();

        // 下側
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2].vertice, hull[hull.Count - 1].vertice, p.vertice, max, mid) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        // 上側
        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            var p = sorted[i];
            while (hull.Count >= lower && Cross(hull[hull.Count - 2].vertice, hull[hull.Count - 1].vertice, p.vertice, max, mid) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    float Cross(Vector3 o, Vector3 a, Vector3 b, int max, int mid)
    {
        var ax = max == 0 ? (a.x - o.x) : max == 1 ? (a.y - o.y) : (a.z - o.z);
        var bz = mid == 0 ? (b.x - o.x) : mid == 1 ? (b.y - o.y) : (b.z - o.z);
        var az = mid == 0 ? (a.x - o.x) : mid == 1 ? (a.y - o.y) : (a.z - o.z);
        var bx = max == 0 ? (b.x - o.x) : max == 1 ? (b.y - o.y) : (b.z - o.z);
        return ax * bz - az * bx;
    }

    /// <summary>
    /// パスを外側にする
    /// </summary>
    /// <param name="path"></param>
    /// <param name="offsetDistance"></param>
    /// <returns></returns>
    private List<Vector3> OffsetPath(List<Vector3> path, float offsetDistance)
    {
        var result = new List<Vector3>();

        for (int i = 0; i < path.Count; i++)
        {
            // 前後の点を取得（ループ対応）
            int prev = (i - 1 + path.Count) % path.Count;
            int next = (i + 1) % path.Count;

            // 前後の接線ベクトルを計算
            Vector3 dirPrev = (path[i] - path[prev]).normalized;
            Vector3 dirNext = (path[next] - path[i]).normalized;

            // 平均接線ベクトル
            Vector3 tangent = (dirPrev + dirNext).normalized;

            // XZ平面上の法線（接線を90度回転）
            Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x).normalized;

            // 外側にオフセット
            result.Add(path[i] + normal * offsetDistance);
        }
        return result;
    }

    /// <summary>
    /// ムーバーオブジェクト作成
    /// </summary>
    private void CreateMoverObject()
    {
        // 既存の動作オブジェクトを無効化
        linearSetting.gameObject.SetActive(false);
        if (loopPathPoints.Count > 4)
        {
            // バケット間隔
            moverPitch = linearSetting.pitch / 1000f;
            moverOffsetPos = linearSetting.offset / 1000f;
            totalLength = linearSetting.length / 1000f;
            curveLength = totalLength / 2 - straightLength;
            for (var i = 0; i < linearSetting.count; i++)
            {
                var mover = new MoverInfo
                {
                    obj = Instantiate(linearSetting.gameObject),
                    pointno = -1,
                    pos = totalLength - moverPitch * i
                };
                // パス上のその距離の位置を取得
                GetPositionOnPath(mover.pos, out Vector3 pos, out Vector3 dir);
                mover.obj.transform.parent = moveObject.transform.parent;
                mover.obj.transform.localPosition = pos;
                if (dir != Vector3.zero)
                {
                    // 円弧の中心からposへの方向を「上」として使う
                    Vector3 toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
                    Quaternion rot = Quaternion.LookRotation(dir, toPos);
                    mover.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                    if (i == 0)
                    {
                        // 初期オフセット
                        moverOffsetAng = mover.obj.transform.localEulerAngles - linearSetting.gameObject.transform.localEulerAngles;
                    }
                    mover.obj.transform.localEulerAngles -= moverOffsetAng;
                }
                mover.obj.SetActive(true);
                movers.Add(mover);
            }
            // 初期値セット
//            MoveBacket(0);
        }
    }

    /// <summary>
    /// パス上のポイント取得
    /// </summary>
    /// <param name="path"></param>
    /// <param name="distance"></param>
    /// <param name="pos"></param>
    /// <param name="dir"></param>
    private void GetPositionOnPath(float distance, out Vector3 pos, out Vector3 dir)
    {
        float accumulated = 0f;
        distance = CalcLinearPos(distance);
        for (int i = 0; i < loopPathPoints.Count - 1; i++)
        {
            float segLen = Vector3.Distance(loopPathPoints[i], loopPathPoints[i + 1]);

            if (accumulated + segLen >= distance)
            {
                float t = (distance - accumulated) / segLen;
                pos = Vector3.Lerp(loopPathPoints[i], loopPathPoints[i + 1], t);
                dir = (loopPathPoints[i + 1] - loopPathPoints[i]).normalized;
                if (!isLinearRvs)
                {
                    dir = -dir;
                }
                return;
            }
            accumulated += segLen;
        }

        // パスの終端
        pos = loopPathPoints[loopPathPoints.Count - 1];
        dir = (loopPathPoints[loopPathPoints.Count - 1] - loopPathPoints[loopPathPoints.Count - 2]).normalized;
        if (!isLinearRvs)
        {
            dir = -dir;
        }
    }

    /*
    /// <summary>
    /// バケット移動
    /// </summary>
    /// <param name="pos"></param>
    protected void MoveBacket(float distance)
    {
        var length = distance - backetPos;
        if (Math.Abs(length) > 0.0001f)
        {
            // 動作中
            // 回転方向が変わってないかチェック
            if (isBacketMoveRvs != (length < 0))
            {
                if (!isBacketMoveRvs)
                {
                    backetCounter += (int)Math.Round(backetPos / unitSetting.backetSetting.pitch);
                    if (backetCounter >= backetCountMax)
                    {
                        backetCounter = 0;
                    }
                }
                else
                {
                    backetCounter -= (int)Math.Round(backetPos / unitSetting.backetSetting.pitch);
                    if (backetCounter < 0)
                    {
                        backetCounter = backetCountMax - 1;
                    }
                }
            }
            else
            {
                isBacketMoveRvs = length < 0;
            }
        }
        backetPos = distance;
        //　動作オフセット
        var backetNext = backetCounter * backetPitch + backetPos / 1000f + backetOffset;
        foreach (var backet in backets)
        {
            var p = (backet.offset + backetNext) % backetLength;
            backet.backetno = (int)(p / backetPitch);
            // パス上のその距離の位置を取得
            GetPositionOnPath(p, out Vector3 pos, out Vector3 dir);
            backet.obj.transform.localPosition = pos;
            if (dir != Vector3.zero)
            {
                // 円弧の中心からposへの方向を「上」として使う
                Vector3 toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
                Quaternion rot = Quaternion.LookRotation(dir, toPos);
                backet.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
            }
        }
    }
    */

    protected override void FixedUpdate()
    {
        foreach (var mover in movers)
        {
            mover.pos = (mover.pos + 0.001f) % totalLength;
            // パス上のその距離の位置を取得
            GetPositionOnPath(mover.pos, out Vector3 pos, out Vector3 dir);
            mover.obj.transform.localPosition = pos;
            if (dir != Vector3.zero)
            {
                // 円弧の中心からposへの方向を「上」として使う
                Vector3 toPos = (pos - new Vector3(center.x, pos.y, center.z)).normalized;
                Quaternion rot = Quaternion.LookRotation(dir, toPos);
                mover.obj.transform.localRotation = rot * Quaternion.Euler(Vector3.zero);
                mover.obj.transform.localEulerAngles -= moverOffsetAng;
            }
        }
    }

    /// <summary>
    /// リニア位置を計算
    /// </summary>
    /// <returns></returns>
    private float CalcLinearPos(float pos)
    {
        var ret = (pos + totalLength) % totalLength;
        if (pos <= straightLength)
        {
        }
        else if (pos <= straightLength + curveLength)
        {
            ret = straightLength + (pos - straightLength) * calcCurveLength / curveLength;
        }
        else if (pos <= straightLength * 2 + curveLength)
        {
            ret = straightLength + calcCurveLength + (pos - straightLength - curveLength);
        }
        else
        {
            ret = straightLength * 2 + calcCurveLength + +(pos - straightLength - straightLength - curveLength) * calcCurveLength / curveLength;
        }
        return ret;
    }
    #endregion 関数定義
}
