using Meta.XR.ImmersiveDebugger.UserInterface;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class ComMcProtocol : ComProtocolBase
{
    /// <summary>
    /// �f�o�C�X���
    /// </summary>
    public enum eDeviceType : byte
    {
        /// <summary>
        /// ����
        /// </summary>
        X = 0x9C,
        /// <summary>
        /// �o��
        /// </summary>
        Y = 0x9D,
        /// <summary>
        /// ���������[
        /// </summary>
        M = 0x90,
        /// <summary>
        /// ���b�`�����[
        /// </summary>
        L = 0x92,
        /// <summary>
        /// �����N�����[
        /// </summary>
        B = 0xA0,
        /// <summary>
        /// �f�[�^
        /// </summary>
        D = 0xA8,
        /// <summary>
        /// �����N
        /// </summary>
        W = 0xB4,
        /// <summary>
        /// �t�@�C�����W�X�^
        /// </summary>
        R = 0xAF,
        /// <summary>
        /// �t�@�C�����W�X�^
        /// </summary>
        ZR = 0xB0,
    }

    /// <summary>
    /// �A�N�Z�X�^�C�v
    /// </summary>
    private enum eAccesstype
    {
        Read = 0,
        Write
    }

    /// <summary>
    /// �r�b�g���W�X�^��`
    /// </summary>
    protected override List<string> regTypeBit
    {
        get
        {
            return new List<string>(new string[] { "M", "X", "Y", "L", "B" });
        }
    }

    /// <summary>
    /// �r�b�g���W�X�^��`
    /// </summary>
    protected override List<string> regTypeBit16
    {
        get
        {
            return new List<string>(new string[] { "X", "Y", "B" });
        }
    }

    /// <summary>
    /// 16bit���W�X�^��`
    /// </summary>
    protected override List<string> regTypeData16
    {
        get
        {
            return new List<string>(new string[] { "D", "W", "R", "ZR" });
        }
    }

    /// <summary>
    /// �ꊇ��M�ݒ�
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 960;
        }
    }

    /// <summary>
    /// �r�b�g��
    /// </summary>
    public override int BIT_COUNT
    {
        get
        {
            return 16;
        }
    }

    /// <summary>
    /// �ǂݏo���R�}���h
    /// </summary>
    protected ushort ReadCommand = 0x0401;

    /// <summary>
    /// �������݃R�}���h
    /// </summary>
    protected ushort WriteCommand = 0x1401;

    /// <summary>
    /// �Œ�R�}���h
    /// </summary>
    protected ushort ReadSubCommand = 0;

    /// <summary>
    /// �J�n����
    /// </summary>
    protected override void Start()
    {
        base.Start();

        if (!GlobalScript.mcprotocols.ContainsKey(Name))
        {
            GlobalScript.mcprotocols.Add(Name, this);
        }
    }

    /// <summary>
    /// �d���쐬
    /// </summary>
    /// <param name="data"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    protected override List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong> values = null)
    {
        // �v���d��
        var message = new List<byte>
        {
            // �T�u�w�b�_
            0x50, 0x00,
            // �l�b�g���[�N�ԍ�
            (byte)directData.NetAddress,
            // PC�ԍ�
            (byte)directData.PcNo,
            // �v���惆�j�b�gI/O�ԍ�
            0xFF, 0x03,
            // �v���惆�j�b�g�ǔԍ�
            0x00
        };
        // �{��
        var body = new List<byte>();
        byte deviceType = (byte)Enum.Parse(typeof(eDeviceType), data.RegisterType);
        ushort command = values == null ? ReadCommand : WriteCommand;
        ushort subcommand = values == null ? ReadSubCommand : (ushort)(regTypeBit.Contains(data.RegisterType) ? 1 : 0);

        // �Ď��^�C�}
        ushort timer = 0x0010;
        body.AddRange(BitConverter.GetBytes(timer));
        // �R�}���h
        body.AddRange(BitConverter.GetBytes(command));
        // �T�u�R�}���h
        body.AddRange(BitConverter.GetBytes(subcommand));
        // �擪�f�o�C�X�ԍ�
        body.AddRange(BitConverter.GetBytes(data.RegisterNo));
        body.RemoveAt(body.Count - 1);
        // �f�o�C�X�R�[�h
        body.Add(deviceType);
        // �f�o�C�X�_��
        if (values == null)
        {
            // ���[�h
            var dataCount = data.AllDataCount;
            if (regTypeBit.Contains(data.RegisterType))
            {
                // �r�b�g�Ȃ�
                if (dataCount % BIT_COUNT == 0)
                {
                    dataCount /= BIT_COUNT;
                }
                else
                {
                    dataCount = (int)Math.Ceiling((decimal)dataCount / BIT_COUNT);
                }
            }
            body.AddRange(BitConverter.GetBytes((ushort)dataCount));
        }
        else
        {
            // ���C�g
            body.AddRange(BitConverter.GetBytes((ushort)values.Count));
            if (subcommand == 1)
            {
                // �r�b�g�f�o�C�X�p
                for (int i = 0; i < values.Count; i += 2)
                {
                    byte tmp = 0;
                    if (values.Count > i + 1)
                    {
                        tmp = (byte)values[i + 1];
                    }
                    tmp += (byte)(values[i] << 4);
                    body.Add(tmp);
                }
            }
            else
            {
                // ���[�h�f�o�C�X�p
                for (int i = 0; i < values.Count; i++)
                {
                    body.AddRange(BitConverter.GetBytes((ushort)values[i]));
                }
            }
        }
        // �R�}���h��
        message.AddRange(BitConverter.GetBytes((ushort)body.Count));
        // �{��
        message.AddRange(body);
        return message;
    }

    /// <summary>
    /// ��M�f�[�^���͏���
    /// </summary>
    /// <param name="datas"></param>
    /// <returns></returns>
    protected override bool AnalysysMessage(KMXDBSetting data, List<byte> datas)
    {
        var buff = datas.ToArray();
        var errCode = BitConverter.ToInt16(buff, 0);
        // �I���R�[�h��0�Ȃ��M����
        if (errCode == 0)
        {
            var index = 0;
            if (regTypeBit.Contains(data.RegisterType))
            {
                // �r�b�g�f�[�^
                for (var i = 2; i < buff.Length; i++)
                {
                    for (var j = 0; j < 8; j++)
                    {
                        var value = (buff[i] & (1 << j)) != 0 ? 1 : 0;
                        if (index < data.values.Count)
                        {
                            if (data.values[index] != null)
                            {
                                data.values[index].Value = value;
                            }
                        }
                        else
                        {
                            break;
                        }
                        index++;
                    }
                }
            }
            else
            {
                // ���[�h�f�[�^
                for (var i = 2; i < buff.Length; i += sizeof(ushort))
                {
                    if (index < data.values.Count)
                    {
                        if (data.values[index] != null)
                        {
                            data.values[index].Value = BitConverter.ToInt16(buff, i);
                        }
                    }
                    else
                    {
                        break;
                    }
                    index++;
                }
            }
        }
        else
        {
            return false;
        }
        return true;
    }
}
