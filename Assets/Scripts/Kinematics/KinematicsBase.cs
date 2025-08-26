using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using XCharts.Runtime;

public class KinematicsBase : UseTagBaseScript
{
    #region クラス
    /// <summary>
    /// テストポジション用クラス
    /// </summary>
    public class TestPosition
    {
        public float min = 0;
        public float max = 100;
        public float target = 0;
        public bool isRvs;
        public float step = 1;
        public float range = 0;
    }
    #endregion クラス

    #region 変数
    /// <summary>
    /// ベースオブジェクト
    /// </summary>
    protected GameObject baseObject;
    #endregion 変数

    #region 関数
    /// <summary>
    /// モデル再構築
    /// </summary>
    public void ModelRestruct()
    {
        RenewParameter();

        ModelRestructProcess();

        SetCollision();
    }

    /// <summary>
    /// パラメータ更新
    /// </summary>
    protected virtual void RenewParameter()
    {
    }

    /// <summary>
    /// モデル再構築処理
    /// </summary>
    protected virtual void ModelRestructProcess()
    {
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected virtual void SetCollision()
    {
    }

    /// <summary>
    /// 当たり判定追加
    /// </summary>
    protected virtual void SetCollision(UnitSetting unitSetting)
    {
    }

    /// <summary>
    /// テスト位置を更新する
    /// </summary>
    public virtual void RenewTestPosition()
    {
    }

    /// <summary>
    /// 次のテスト位置を取得する
    /// </summary>
    /// <param name="pos"></param>
    public float RenewNextPosition(TestPosition pos)
    {
        var step = pos.step;
        if (pos.range != 0)
        {
            step = UnityEngine.Random.Range(step - pos.range / 2, step + pos.range / 2);
        }
        if (pos.isRvs)
        {
            pos.target -= step;
            pos.isRvs = pos.target >= pos.min;
        }
        else
        {
            pos.target += step;
            pos.isRvs = pos.target >= pos.max;
        }
        return pos.target;
    }

    /// <summary>
    /// 値チェック
    /// </summary>
    /// <param name="value"></param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    protected int CheckRange(int value, int min, int max)
    {
        if (min < max)
        {
            if (value < min)
            {
                value = min;
            }
            if (value > max)
            {
                value = max;
            }
        }
        return value;
    }

    /// <summary>
    /// 値チェック
    /// </summary>
    /// <param name="value"></param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    protected float CheckRangeF(float value, float min, float max)
    {
        if (min < max)
        {
            if (value < min)
            {
                value = min;
            }
            if (value > max)
            {
                value = max;
            }
        }
        return value;
    }

    /// <summary>
    /// 親オブジェクト挿入
    /// </summary>
    protected void InsertParent(Transform parent, Transform child)
    {
        parent.transform.parent = child.parent;
        parent.transform.localPosition = child.localPosition;
        parent.transform.localEulerAngles = child.localEulerAngles;
        child.parent = parent.transform;
    }
    #endregion 関数
}
