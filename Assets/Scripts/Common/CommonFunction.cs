using System;
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
}