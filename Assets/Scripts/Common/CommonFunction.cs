using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;


public static class CommonFunction
{
    #region ���̌���
    /// <summary>
    /// ���̌����ɂ����锻�ʎ�b^2 - 4ac���v�Z����
    /// </summary>
    /// <param name="a">a�ϐ�</param>
    /// <param name="b">b�ϐ�</param>
    /// <param name="c">c�ϐ�</param>
    /// <returns></returns>
    public static float Discriminant(float a, float b, float c)
    {
        float result = b * b - 4 * a * c;

        return result;
    }

    /// <summary>
    /// ������
    /// </summary>
    /// <param name="discriminant">���ʎ�b^2 - 4ac</param>
    /// <param name="a">a�ϐ�</param>
    /// <param name="b">b�ϐ�</param>
    /// <param name="c">c�ϐ�</param>
    /// <returns></returns>
    public static float QuadraticFormula_Real(float discriminant, float a, float b, float c)
    {
        float plusResult;

        // �v���X���������Ȃ̂ł��ꂾ���v�Z����
        plusResult = (-b + Mathf.Sqrt(discriminant)) / (2 * a);
        //minusResult = (-b - Mathf.Sqrt(discriminant)) / (2 * a);

        return plusResult;
    }

    /// <summary>
    /// ������
    /// </summary>
    /// <param name="discriminant">���ʎ�b^2 - 4ac</param>
    /// <param name="a">a�ϐ�</param>
    /// <param name="b">b�ϐ�</param>
    /// <param name="c">c�ϐ�</param>
    /// <returns></returns>
    public static float QuadraticFormula_Complex(float discriminant, float a, float b, float c)
    {
        var sqrtDiscriminant = Complex.Sqrt(discriminant);

        Complex root1 = (-b + sqrtDiscriminant) / (2 * a);
        //Complex root2 = (-b - sqrtDiscriminant) / (2 * a);

        return (float)root1.Real;
    }

    #endregion ���̌���
}