using Parameters;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CeilingArmRobot : ArmRobot
{
    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public override void SetTarget(float x, float y, float z)
    {
        base.SetTarget(-x, -y, -90);
    }

    /// <summary>
    /// ロボットの定義で使う型（天吊りの定義からアームの名前を読む）
    /// </summary>
    protected override RobotType DefinitionType
    {
        get { return RobotType.CEILING_ARM; }
    }

    /// <summary>
    /// SetTarget に渡す形から目標の形へ（x,y の符号を戻す。z は使わないので 0）
    /// </summary>
    protected override Vector3 ToInputTarget(Vector3 value)
    {
        return new Vector3(-value.x, -value.y, 0f);
    }
}