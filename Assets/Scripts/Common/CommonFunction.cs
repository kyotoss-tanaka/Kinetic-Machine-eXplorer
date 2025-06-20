using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;


public static class CommonFunction
{
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
}