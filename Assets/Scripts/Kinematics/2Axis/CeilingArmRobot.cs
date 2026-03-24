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
}