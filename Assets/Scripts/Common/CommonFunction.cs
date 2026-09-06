using System;
using Parameters;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

public static class CommonFunction
{
    // Camera.main は内部で FindGameObjectWithTag を呼ぶため毎フレーム使用は重い。キャッシュして使う。
    private static UnityEngine.Camera s_mainCamera;
    /// <summary>Camera.main のキャッシュ。破棄/無効化時のみ再取得（実行時のカメラ切替にも追従）。</summary>
    public static UnityEngine.Camera MainCamera
    {
        get
        {
            if (s_mainCamera == null || !s_mainCamera.isActiveAndEnabled)
            {
                s_mainCamera = UnityEngine.Camera.main;
            }
            return s_mainCamera;
        }
    }

    #region 解の公式
    /// <summary>
    /// 解の公式における判別式b^2 - 4acを計算する
    /// </summary>
    /// <param name="a">a変数</param>
    /// <param name="b">b変数</param>
    /// <param name="c">c変数</param>
    /// <returns></returns>
    public static float Discriminant(float a, float b, float c)
    {
        float result = b * b - 4 * a * c;

        return result;
    }

    /// <summary>
    /// 実数解
    /// </summary>
    /// <param name="discriminant">判別式b^2 - 4ac</param>
    /// <param name="a">a変数</param>
    /// <param name="b">b変数</param>
    /// <param name="c">c変数</param>
    /// <returns></returns>
    public static float QuadraticFormula_Real(float discriminant, float a, float b, float c)
    {
        float plusResult;

        // プラス解が正解なのでこれだけ計算する
        plusResult = (-b + Mathf.Sqrt(discriminant)) / (2 * a);
        //minusResult = (-b - Mathf.Sqrt(discriminant)) / (2 * a);

        return plusResult;
    }

    /// <summary>
    /// 虚数解
    /// </summary>
    /// <param name="discriminant">判別式b^2 - 4ac</param>
    /// <param name="a">a変数</param>
    /// <param name="b">b変数</param>
    /// <param name="c">c変数</param>
    /// <returns></returns>
    public static float QuadraticFormula_Complex(float discriminant, float a, float b, float c)
    {
        var sqrtDiscriminant = Complex.Sqrt(discriminant);

        Complex root1 = (-b + sqrtDiscriminant) / (2 * a);
        //Complex root2 = (-b - sqrtDiscriminant) / (2 * a);

        return (float)root1.Real;
    }

    #endregion 解の公式

    #region メソッド
    /// <summary>
    /// シーンパスを取得する
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static List<string> GetScenePath(GameObject obj)
    {
        var path = new List<string>();
        path.Add(obj.name);
        Transform current = obj.transform;

        while (current.parent != null)
        {
            current = current.parent;
            path.Add(current.name);
        }
        return path;
    }

    /// <summary>
    /// 削除処理
    /// </summary>
    /// <param name="obj"></param>
    public static void DestroyWithMaterials(GameObject obj)
    {
        if (obj != null)
        {
            foreach (var r in obj.GetComponentsInChildren<Renderer>(true))
            {
                if (r.material != null)
                {
                    UnityEngine.Object.Destroy(r.material);
                }
            }
            UnityEngine.Object.Destroy(obj);
        }
    }

    #region デバッグ用
    private static bool isDebug = false;
    private static System.Diagnostics.Stopwatch swDebug = new();
    private static long prvLap = 0;

    /// <summary>
    /// デバッグ用情報初期化
    /// </summary>
    public static void DebugInfoInit()
    {
        swDebug.Restart();
        prvLap = 0;
    }

    /// <summary>
    /// デバッグログ
    /// </summary>
    public static void DebugLog(string message, bool isForce = false)
    {
        if (isDebug || isForce)
        {
            Debug.Log($"{swDebug.ElapsedMilliseconds}({swDebug.ElapsedMilliseconds - prvLap})msec : {message}");
            prvLap = swDebug.ElapsedMilliseconds;
        }
    }
    #endregion デバッグ用

    #endregion メソッド

    /// <summary>
    /// 動作テーブルから、指定時刻を挟む前後の行を求める。datas は time 昇順である前提
    /// （利用側の初期化で OrderBy 済み）。
    ///
    /// 元は LastOrDefault / FirstOrDefault を使っていたが、述語つきの LastOrDefault は
    /// 一致を探すためにリスト全体を走査し、さらに時刻を捕まえるクロージャを毎回確保する。
    /// time/value が decimal で比較自体も重く、テーブルは最大557行あるため、
    /// 参照するオブジェクトの数に比例して積み上がっていた（段ボール9個で6.25ms・GC 23.4KB）。
    /// 再生ヘッドはほぼ前進するので、前回位置から進める形にすれば1回あたり数比較で済む。
    /// 戻った場合（リセットや別個体の使い回し）も後方へ walk して自力で復帰する。
    /// </summary>
    /// <param name="datas">動作テーブル（time昇順）</param>
    /// <param name="t">求める時刻</param>
    /// <param name="index">前回の探索位置。呼び出しごとに更新する</param>
    /// <param name="before">time &lt;= t を満たす最後の行。無ければ null</param>
    /// <param name="after">time &gt;= t を満たす最初の行。無ければ null</param>
    public static void FindActionSpan(List<ActionData> datas, decimal t, ref int index, out ActionData before, out ActionData after)
    {
        before = null;
        after = null;
        var count = datas.Count;
        if (count == 0)
        {
            return;
        }
        if (index >= count)
        {
            index = count - 1;
        }
        // time <= t を満たす最後の位置まで進める／戻す
        while ((index + 1 < count) && (datas[index + 1].time <= t))
        {
            index++;
        }
        while ((index >= 0) && (datas[index].time > t))
        {
            index--;
        }
        before = (index >= 0) ? datas[index] : null;
        // time >= t を満たす最初の位置。同一時刻が並ぶ場合は先頭を採る（LastOrDefault/FirstOrDefault と同じ結果にする）
        var next = index + 1;
        if ((index >= 0) && (datas[index].time == t))
        {
            next = index;
            while ((next > 0) && (datas[next - 1].time == t))
            {
                next--;
            }
        }
        after = (next < count) ? datas[next] : null;
    }

}