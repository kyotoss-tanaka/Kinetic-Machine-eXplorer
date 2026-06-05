using System.Collections.Generic;
using UnityEngine;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// TimeChartController の使用例。
    /// RegisterDevices() でデバイスを登録し、
    /// RecordSignals() で毎フレームデータを渡す。
    /// </summary>
    public class MachineTimeChart : TimeChartController
    {
        // ----------------------------------------------------------------
        // デバイス登録
        // ----------------------------------------------------------------
        protected override void RegisterDevices()
        {
            foreach (var tm in GlobalScript.timeChartDatas)
            {
                foreach (var dev in tm.datas)
                {
                    if (dev.devType == Parameters.TimeChartDevice.DeviceType.Internal)
                    {
                        var cyl = new CylinderDef
                        {
                            Name = dev.name,
                        };
                        foreach (var pos in dev.positions)
                        {
                            if (cyl.Positions.Find(d => d.PositionName == pos.name) == null)
                            {
                                cyl.Positions.Add(new CylinderPositionDef
                                {
                                    PositionName = pos.name,
                                    CommandChannelName = pos.tagIn,
                                    ASChannelName = pos.tagOut,
                                    PosValue = pos.pos
                                });
                            }
                        }
                        RegisterCylinder(cyl);
                        RegisterGroup(dev.group, dev.name);
                    }
                    else if (dev.devType == Parameters.TimeChartDevice.DeviceType.External)
                    {
                        float minPos = float.MaxValue, maxPos = float.MinValue;
                        foreach (var pos in dev.positions)
                        {
                            if (pos.pos < minPos) minPos = pos.pos;
                            if (pos.pos > maxPos) maxPos = pos.pos;
                        }
                        // min==maxの場合（位置が1つだけ）は範囲を広げる
                        if (Mathf.Approximately(minPos, maxPos)) { minPos -= 1f; maxPos += 1f; }

                        RegisterMechanism(new MechanismDef
                        {
                            Name = dev.name,
                            MinValue = minPos,
                            MaxValue = maxPos,
                        });
                        RegisterGroup(dev.group, dev.name);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // 毎フレームのデータ入力
        // ----------------------------------------------------------------
        protected override void RecordSignals()
        {
            /*
            SetCylinder("CYL1", cyl1FwdCmd, cyl1FwdAS, cyl1BwdCmd, cyl1BwdAS);
            SetSensor("SEN1", sen1IsOn);
            SetMechanism("MECH1", mech1Pos);
            */
        }
    }
}