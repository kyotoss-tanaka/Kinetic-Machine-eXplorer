using Meta.XR.ImmersiveDebugger.UserInterface;
using NUnit;
using Palmmedia.ReportGenerator.Core;
using Parameters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class ComMicks : ComProtocolBase
{

    #region �N���X
    /// <summary>�R�}���h�t�H�[�}�b�g</summary>
    public class ClsComFormat
    {
        /// <summary>�R�}���h�R�[�h</summary>
        public string strCmdCode;
        public int intCmdCode;
        /// <summary>�T�u�R�}���h</summary
        public string strSubCmd;
        public byte bytSubCmd;
        /// <summary>�R�}���hID</summary>
        public string strCmdId;
        public byte bytCmdId;
        /// <summary>�f�[�^��</summary>
        public string strDataNum;
        public int intDataNum;
        /// <summary>�f�[�^��</summary>
        public string strDataSize;
        public byte bytDataSize;
        /// <summary>�f�[�^</summary>
        public List<uniLongAllData> lstData = new List<uniLongAllData>();
        /// <summary>������f�[�^ 8000�Ԉȍ~�̓���R�}���h�p</summary>
        public string strData;
        /// <summary>CF�A�N�Z�X�R�}���h</summary>
        public bool blnCFCommand;
        /// <summary>�^�C���A�E�g����</summary>
        public int intTimeOut = 0;
        /// <summary>�G���[���</summary>
        public bool isError = false;
        /// <summary>��e�ʒʐM</summary>
        public bool IsLargeCapacity = false;
    }
    #endregion �N���X

    #region ��
    /// <summary>MICKS�o�[�W����</summary>
    public enum EnmMicksVer
    {
        /// <summary>MICKS1</summary>
        MICKS_VER1,
        /// <summary>MICKS2</summary>
        MICKS_VER2,
        /// <summary>MICKS2K</summary>
        MICKS_VER2K,
    }

    /// <summary>
    /// �T�u�R�}���h
    /// </summary>
    private enum eReadSubCommand
    {
        /// <summary>
        /// 32bit�f�o�C�X�A���ꃌ�W�X�^
        /// </summary>
        Device32,
        /// <summary>
        /// 64bitG�AL�AGF�ALF���W�X�^
        /// </summary>
        Device64,
        /// <summary>
        /// 64bitG�AL���W�X�^�f�o�C�X�R�[�h�擾
        /// </summary>
        DeviceCode
    }

    /// <summary>
    /// �R�}���h���
    /// </summary>
    public enum EnmCommCommand
    {
        //*********************
        // �ʏ�R�}���h
        //*********************
        /// <summary>���W�X�^�ݒ�</summary>
        SetRegister = 0x100,
        /// <summary>���W�X�^�擾</summary>
        GetRegister = 0x101,
        /// <summary>I/O�f�o�C�X�P�r�b�g�o��</summary>
        SetIODeviceBit = 0x102,
        /// <summary>I/O�f�o�C�X�}���`�r�b�g�o��</summary>
        SetIODeviceMultiBit = 0x103,

        /// <summary>��POS�p�����[�^���擾</summary>
        GetActivatedPosParam = 0x160,

        /// <summary>�@�\�̓o�^</summary>
        SetMecha = 0x200,
        /// <summary>�@�\�̎擾</summary>
        GetMecha = 0x201,
        /// <summary>�@�\�o�^�G���A�̎擾</summary>
        GetMechArea = 0x202,

        /// <summary>�S�@�\�ʒu���ꊇ�擾</summary>
        GetAllMechPos = 0x250,

        /// <summary>�A���v�p�����[�^�ݒ� SSC-NET�V</summary>
        SetAmpParameterNet3 = 0x300,
        /// <summary>�A���v�p�����[�^�擾 SSC-NET�V</summary>
        GetAmpParameterNet3 = 0x301,
        /// <summary>�A���v�p�����[�^�ݒ� MECHATOROLINK�V</summary>
        SetAmpParameterMech3 = 0x310,
        /// <summary>�A���v�p�����[�^�擾 MECHATOROLINK�V</summary>
        GetAmpParameterMech3 = 0x311,
        /// <summary>�A���v�p�����[�^�ݒ� SSC-NET�V</summary>
        SetAmpParameterMC120 = 0x320,
        /// <summary>�A���v�p�����[�^�擾 SSC-NET�V</summary>
        GetAmpParameterMC120 = 0x321,
        /// <summary>�A���v�p�����[�^�ݒ� SSC-NET�V</summary>
        SetAmpParameterMC210 = 0x322,
        /// <summary>�A���v�p�����[�^�擾 SSC-NET�V</summary>
        GetAmpParameterMC210 = 0x323,
        /// <summary>�A���v�p�����[�^�ݒ� SSC-NET�V</summary>
        SetAmpParameterMga023 = 0x324,
        /// <summary>�A���v�p�����[�^�擾 SSC-NET�V</summary>
        GetAmpParameterMga023 = 0x325,
        /// <summary>�A���v�p�����[�^�ݒ� EtherCAT</summary>
        SetAmpParameterEcat = 0x326,
        /// <summary>�A���v�p�����[�^�擾 EtherCAT</summary>
        GetAmpParameterEcat = 0x327,

        /// <summary>�v���O��������</summary>
        OperateProgram = 0x400,
        /// <summary>�u���[�N�|�C���g�ݒ�</summary>
        SetBreakPoint = 0x401,
        /// <summary>�v���O�����A���[����M</summary>
        GetProgramAlm = 0x402,
        /// <summary>�v���O�����A���[���R�[�h��M</summary>
        GetProgramAlmCode = 0x403,
        /// <summary>�v���O�����҂�I/O��M</summary>
        GetProgramWaitIo = 0x404,
        /// <summary>�v���O�����s���擾</summary>
        SetProgramLineNum = 0x405,
        /// <summary>�v���O�����g���[�XI/O�Z�b�g</summary>
        SetProgramTraceTrg = 0x406,

        /// <summary>�I�u�W�F�N�g�O���[�v���擾</summary>
        GetObjectGroupInfo = 0x500,
        /// <summary>�I�u�W�F�N�g���擾</summary>
        GetObjectInfo = 0x501,
        /// <summary>�I�u�W�F�N�g�͈͏��擾</summary>
        GetObjectRangeInfo = 0x502,
        /// <summary>�I�u�W�F�N�g�o�^���擾</summary>
        GetObjectRegInfo = 0x503,
        /// <summary>�I�u�W�F�N�g�T�u�͈͏��擾</summary>
        GetObjectSubRangeInfo = 0x504,
        /// <summary>�I�u�W�F�N�g�T�uI/O�͈͏��擾</summary>
        GetObjectSubIoRangeInfo = 0x505,
        /// <summary>�I�u�W�F�N�g�o�^���擾(RAM10��Ver)</summary>
        GetObjectRegInfoEx = 0x506,

        /// <summary>�������擾</summary>
        GetDiffRegInfo = 0x540,

        /// <summary>�J���|�W���擾</summary>
        GetCamPosInfo = 0x550,
        /// <summary>�J���|�W���擾(�g����)</summary>
        GetCamPosInfoEx = 0x551,

        /// <summary>�G���R�[�_�������擾</summary>
        GetEncSyncInfo = 0x560,
        /// <summary>�G���R�[�_������ƃe�[�u�����擾</summary>
        GetEncSyncWorkTable = 0x561,
        /// <summary>�G���R�[�_�����e�[�u�����擾</summary>
        GetEncSyncEncSyncTable = 0x562,
        /// <summary>�G���R�[�_�����e�[�u���f�[�^�擾</summary>
        GetEncSyncEncSyncData = 0x563,
        /// <summary>�G���R�[�_�����o�^��Ԏ擾</summary>
        GetEncSyncRegInfo = 0x564,

        /// <summary>�J�������擾</summary>
        GetCameraInfo = 0x570,
        /// <summary>�J�����f�[�^�擾</summary>
        GetCameraData,
        /// <summary>�J�����g���K�Z�b�g</summary>
        SetCameraTrg,
        /// <summary>�J�����f�[�^�N���A</summary>
        ClrCameraData,
        /// <summary>�J�����S�f�[�^�N���A</summary>
        ClrCameraAllData,

        /// <summary>�L�����u���[�V�������擾</summary>
        GetGlblGetCalibInfo = 0x580,
        /// <summary>�O���[�o�����W�f�[�^�擾</summary>
        GetGlblGetCdntData,
        /// <summary>�O���[�o�����W���ݒ�</summary>
        SetGlblGetCdntData,
        /// <summary>�L�����u���[�V��������</summary>
        SetGlblCalibOpt,

        /// <summary>I/O�t�B���^�[���擾</summary>
        GetIoFilterInfo = 0x590,

        /// <summary>�A���[�������擾</summary>
        GetAlarmLog = 0x600,
        /// <summary>���O�����擾</summary>
        GetMicksLog,
        /// <summary>ABS���擾</summary>
        GetAbsInfo,
        /// <summary>SRAMABS���擾</summary>
        GetSramAbsInfo,
        /// <summary>SRAM�f�[�^�擾</summary>
        GetSramData,

        /// <summary>�������擾</summary>
        GetMemory = 0x650,
        /// <summary>PCI�������擾</summary>
        GetPciMemory,
        /// <summary>CC-Link�������擾</summary>
        GetCCLinkMemory,
        /// <summary>SSCNET�������擾</summary>
        GetSscnetMemory,
        /// <summary>�T�[�{���j�b�g�������擾</summary>
        GetServoUnitMemory,
        /// <summary>�T�[�{�G���[���擾</summary>
        GetServoErrInfo,
        /// <summary>�������ݒ�</summary>
        SetMemory = 0x660,

        /// <summary>����f�[�^�Z�b�g�O����</summary>
        SetMeasureDataPre = 0x700,
        /// <summary>����f�[�^�Z�b�g�㏈��</summary>
        SetMeasureDataAfter = 0x701,
        /// <summary>����Ώېݒ�</summary>
        SetMeasureObject = 0x702,
        /// <summary>����Ώێ擾</summary>
        GetMeasureObject = 0x703,
        /// <summary>����f�[�^�g���K�ݒ�</summary>
        SetMeasureTrigger = 0x704,
        /// <summary>����f�[�^�g���K�擾</summary>
        GetMeasureTrigger = 0x705,
        /// <summary>����f�[�^�擾</summary>
        GetMeasureData = 0x706,
        /// <summary>�����Ԃ̎擾</summary>
        GetMeasureCondition = 0x707,
        /// <summary>����̒��~</summary>
        StopMeasure = 0x708,

        /// <summary>�^�C���`���[�g�ݒ�擾</summary>
        GetTimeChartSetting = 0x720,
        /// <summary>�^�C���`���[�g�f�[�^�擾</summary>
        GetTimeChartData = 0x721,

        // ���A���^�C�����O�f�[�^�擾
        GetRtLogData = 0x725,
        /// <summary>���A���^�C�����O�ŐV�f�[�^�擾</summary>
        GetRtLogLatestData,

        /// <summary>1��sec�������CPU�J�E���g�擾</summary>
        GetCpu1usecCount = 0x730,
        /// <summary>I/O�����f�[�^�擾</summary>
        GetIoHistoryData = 0x731,

        /// <summary>�^�C���X�^���v�J�n</summary>
        StartTimeStamp = 0x750,
        /// <summary>�^�C���X�^���v���~</summary>
        StopTimeStamp,
        /// <summary>�^�C���X�^���v��Ԏ擾</summary>
        GetTimeStampInfo,
        /// <summary>�^�C���X�^���v�f�[�^�擾</summary>
        GetTimeStampData,

        /// <summary>�h���C�u���R�[�_���擾</summary>
        GetDrvrecInfo = 0x760,
        /// <summary>�h���C�u���R�[�_�f�[�^�擾</summary>
        GetDrvrecData,
        /// <summary>�h���C�u���R�[�_�p�����[�^�擾</summary>
        GetDrvrecPrmData,

        /// <summary>�V�X�e�����Ԃ̐ݒ�</summary>
        SetSystemTime = 0x800,
        /// <summary>�ώZ���Ԃ̃N���A</summary>
        ClearAddTime,

        /// <summary>�p�����[�^CF����</summary>
        WriteCFParameter = 0x900,
        /// <summary>�v���O����CF����</summary>
        WriteCFProgram = 0x901,

        /// <summary>OS�t�@�C���A�b�v���[�h</summary>
        OSFileUpLoad = 0xA00,
        /// <summary>�V�X�e���e�X�g</summary>
        SystemTest = 0xA01,

        /// <summary>�f�[�^�������擾</summary>
        GetDataHistoryData = 0xB00,
        /// <summary>�f�[�^�����|�C���g���擾</summary>
        GetDataHistoryPoint = 0xB01,

        //*****************************************
        // �f�o�b�O�R�}���h 0x7000�`
        //*****************************************
        /// <summary>�@�\������</summary>
        MechInit = 0x7000,
        /// <summary>���_�Z�b�g</summary>
        SetOrg = 0x7001,
        /// <summary>�T�[�{OFF</summary>
        ServoOff = 0x7002,
        /// <summary>�@�\��~</summary>
        MechStop = 0x7003,
        /// <summary>�@�\������~</summary>
        MechSlowDown = 0x7004,
        //*****************************************
        // �@�\�ݒ�R�}���h 0x7800�`
        //*****************************************
        /// <summary>�o�C�i���ʐM</summary>
        FuncBinCom = 0x7800,
        //*****************************************
        // ����R�}���h 0x8000�`
        //*****************************************
        /// <summary>�v���O�������M</summary>
        SetProgram = 0x8000,
        /// <summary>�v���O������M</summary>
        GetProgram = 0x8001,
    }


    /// <summary>���W�X�^�R�[�h</summary>
    public enum EnmRegCode : int
    {
        /// <summary></summary>
        Const = 20,
        /// <summary></summary>
        G,
        /// <summary></summary>
        L,
        /// <summary></summary>
        GF = 24,
        /// <summary></summary>
        LF,
        /// <summary></summary>
        S = 27,
        /// <summary></summary>
        A,
        /// <summary></summary>
        R,
        /// <summary></summary>
        P,
        /// <summary></summary>
        RWr,
        /// <summary></summary>
        RWw,
        /// <summary></summary>
        X = 50,
        /// <summary></summary>
        Y,
        /// <summary></summary>
        M,
        /// <summary></summary>
        RX,
        /// <summary></summary>
        RY,
        /// <summary></summary>
        LM,
        /// <summary></summary>
        RWrB,
        /// <summary></summary>
        RWwB,
        /// <summary>60 �` 99:�\��</summary>
        Pointer = 60,
        GP,
        LP,
        FP,
        GFP,
        LFP,
        Address = 80,
        GA,
        LA,
        F,
        GFA,
        LFA,
        /// <summary></summary>
        SBit = 100,
        /// <summary></summary>
        ABit,
        /// <summary></summary>
        RBit,
        /// <summary></summary>
        RWrBit,
        /// <summary></summary>
        RWwBit,
        /// <summary></summary>
        GBit,
        /// <summary></summary>
        LBit,
        /// <summary></summary>
        PBit,
    }
    /// <summary>
    /// R���W�X�^��`
    /// </summary>
    public enum EnmRRegDefine
    {
        MECH_STAT = 0,
        MECH_ACT_STAT,
        MECH_ERR_STAT,
        MECH_WRN_STAT,
        MECH_JOG_STAT,
        MECH_JOG_CMD,
        MECH_DIFF_STAT,
        MECH_SP_FUNC = 9,
        MECH_TYPE = 10,
        MECH_MOTOR1,
        MECH_MOTOR2,
        MECH_MOTOR3,
        MECH_MOTOR4,
        MECH_MOTOR5,
        MECH_MOTOR6,
        MECH_MOTOR7,
        MECH_SCALE = 20,
        MECH_FIN_X,
        MECH_FIN_Y,
        MECH_FIN_Z,
        MECH_FB_X,
        MECH_FB_Y,
        MECH_FB_Z,
        MECH_FIN_ADD,
        MECH_FB_ADD,
        MECH_NOW_CMD,
        MECH_FIN_SPD,
        MECH_TRG_SPD,
        MECH_FIN_ACL,
        MECH_TRG_ACL,
        MECH_FIN_MSPD_P,
        MECH_FIN_MSPD_M,
        MECH_TRG_MSPD_P,
        MECH_TRG_MSPD_M,
        MECH_FIN_MACL_P,
        MECH_FIN_MACL_M,
        MECH_TRG_MACL_P,
        MECH_TRG_MACL_M,
        MECH_DIFF_OBJ_X,
        MECH_DIFF_OBJ_Y,
        MECH_DIFF_OBJ_Z,
    }
    /// <summary>
    /// A���W�X�^��`
    /// </summary>
    public enum EnmARegDefine
    {
        MOTOR_SPD = 10,
        MOTOR_ACL,
        MOTOR_MSPD_P,
        MOTOR_MSPD_M,
        MOTOR_MACL_P,
        MOTOR_MACL_M,
        MOTOR_MERRCNT_P,
        MOTOR_MERRCNT_M,
        MOTOR_MEFC_RL,
        MOTOR_MPEAK_RL,
        MOTOR_SV_FUNC,
        MOTOR_SV_STAT,
        MOTOR_SV_TRG_PLS_L,
        MOTOR_SV_TRG_PLS_H,
        MOTOR_SV_NOW_PLS_L,
        MOTOR_SV_NOW_PLS_H,
        MOTOR_SV_FB_PLS_L,
        MOTOR_SV_FB_PLS_H,
        MOTOR_SV_ENC_REV,
        MOTOR_SV_ENC_ONE_REV,
        MOTOR_SV_ERRCNT,
        MOTOR_SV_ACM_PLC,
        MOTOR_SV_CRNT_FB,
        MOTOR_SV_EFC_LR,
        MOTOR_SV_PEAK_LR,
        MOTOR_SV_ALM_NO,
        MOTOR_SV_RGN_LR,
        MOTOR_SV_FB_SPD,
        MOTOR_SV_BUS_V
    }
    #endregion

    #region �萔
    // �p�P�b�g�����
    /*
    /// <summary>�R�}���h�C���f�b�N�X</summary>
    private int COM_CMD_IDX = 1;
    /// <summary>�R�}���h��</summary>
    private int COM_CMD_LEN = 4;
    /// <summary>�T�u�R�}���h�C���f�b�N�X</summary>
    private int COM_SUBCMD_IDX = 5;
    /// <summary>�T�u�R�}���h��</summary>
    private int COM_SUBCMD_LEN = 1;
    /// <summary>�R�}���hID�C���f�b�N�X</summary>
    private int COM_CMDID_IDX = 6;
    /// <summary>�R�}���hID��</summary>
    private int COM_CMDID_LEN = 1;
    /// <summary>�f�[�^���C���f�b�N�X</summary>
    private int COM_DATANUM_IDX = 7;
    /// <summary>�f�[�^����</summary>
    private int COM_DATANUM_LEN = 2;
    /// <summary>�f�[�^�T�C�Y�C���f�b�N�X</summary>
    private int COM_DATASIZE_IDX = 9;
    /// <summary>�f�[�^�T�C�Y��</summary>
    private int COM_DATASIZE_LEN = 1;
    /// <summary>�f�[�^�C���f�b�N�X</summary>
    private int COM_DATA_IDX = 10;
    */
    /// <summary>�R�}���h�C���f�b�N�X</summary>
    private const int COM_CMD_IDX = 1;
    /// <summary>�R�}���h��</summary>
    private const int COM_CMD_LEN = 4;
    /// <summary>�T�u�R�}���h�C���f�b�N�X</summary>
    private int COM_SUBCMD_IDX { get { return COM_CMD_IDX + COM_CMD_LEN; } }
    /// <summary>�T�u�R�}���h��</summary>
    private const int COM_SUBCMD_LEN = 1;
    /// <summary>�R�}���hID�C���f�b�N�X</summary>
    private int COM_CMDID_IDX { get { return COM_SUBCMD_IDX + COM_SUBCMD_LEN; } }
    /// <summary>�R�}���hID��</summary>
    private const int COM_CMDID_LEN = 1;
    /// <summary>�f�[�^���C���f�b�N�X</summary>
    private int COM_DATANUM_IDX { get { return COM_CMDID_IDX + COM_CMDID_LEN; } }
    /// <summary>�f�[�^����</summary>
    private const int COM_DATANUM_LEN = 2;
    /// <summary>�f�[�^�T�C�Y�C���f�b�N�X</summary>
    private int COM_DATASIZE_IDX { get { return COM_DATANUM_IDX + COM_DATANUM_LEN; } }
    /// <summary>�f�[�^�T�C�Y��</summary>
    private const int COM_DATASIZE_LEN = 1;
    /// <summary>�f�[�^�C���f�b�N�X</summary>
    private int COM_DATA_IDX { get { return COM_DATASIZE_IDX + COM_DATASIZE_LEN; } }

    /// <summary>�f�[�^����</summary>
    private const int COM_DATANUM_LEN_EX = 4;
    /// <summary>�f�[�^�T�C�Y�C���f�b�N�X</summary>
    private int COM_DATASIZE_IDX_EX { get { return COM_DATANUM_IDX + COM_DATANUM_LEN_EX; } }
    /// <summary>�f�[�^�C���f�b�N�X</summary>
    private int COM_DATA_IDX_EX { get { return COM_DATASIZE_IDX_EX + COM_DATASIZE_LEN; } }

    /// <summary>�R�}���h�C���f�b�N�X</summary>
    private const int COM_CMD_IDX_BIN = 1;
    /// <summary>�R�}���h��</summary>
    private const int COM_CMD_LEN_BIN = 2;
    /// <summary>�T�u�R�}���h�C���f�b�N�X</summary>
    private int COM_SUBCMD_IDX_BIN { get { return COM_CMD_IDX_BIN + COM_CMD_LEN_BIN; } }
    /// <summary>�T�u�R�}���h��</summary>
    private const int COM_SUBCMD_LEN_BIN = 1;
    /// <summary>�f�[�^���C���f�b�N�X</summary>
    private int COM_DATANUM_IDX_BIN { get { return COM_SUBCMD_IDX_BIN + COM_SUBCMD_LEN_BIN; } }
    /// <summary>�f�[�^����</summary>
    private const int COM_DATANUM_LEN_BIN = 2;
    /// <summary>�f�[�^�T�C�Y�C���f�b�N�X</summary>
    private int COM_DATASIZE_IDX_BIN { get { return COM_DATANUM_IDX_BIN + COM_DATANUM_LEN_BIN; } }
    /// <summary>�f�[�^�T�C�Y��</summary>
    private const int COM_DATASIZE_LEN_BIN = 1;
    /// <summary>�f�[�^�C���f�b�N�X</summary>
    private int COM_DATA_IDX_BIN { get { return COM_DATASIZE_IDX_BIN + COM_DATASIZE_LEN_BIN; } }

    /// <summary>
    /// �@�\�ő吔
    /// </summary>
    public static int MechMax = 64;
    /// <summary>
    /// ���ꃌ�W�X�^�ő吔
    /// </summary>
    public static int SpRegMax = 100;
    /// <summary>
    /// L���W�X�^�ő吔
    /// </summary>
    public static int LRegMax = 2000;
    /// <summary>
    /// �f�[�^�����I�t�Z�b�g
    /// </summary>
    private static int DataCountOffset = 6;
    /// <summary>
    /// �f�[�^�o�C�g�ő吔�I�t�Z�b�g
    /// </summary>
    private static int DataByteMaxOffset = 8;
    /// <summary>
    /// �f�[�^�I�t�Z�b�g
    /// </summary>
    private static int DataOffset = 9;

    /// <summary>
    /// STX
    /// </summary>
    private byte STX = 0x02;

    /// <summary>
    /// ETX
    /// </summary>
    private byte ETX = 0x03;

    /// <summary>
    /// ACK
    /// </summary>
    private byte ACK = 0x06;

    /// <summary>
    /// NAK
    /// </summary>
    private byte NAK = 0x15;

    /// <summary>
    /// SYN
    /// </summary>
    private byte SYN = 0x16;

    /// <summary>
    /// CR
    /// </summary>
    private byte CR = 0x0D;

    /// <summary>
    /// �^�C���`���[�g�f�[�^�ő�
    /// </summary>
    private int TimeChartDataMax = 5000;

    /// <summary>
    /// �@�\��1�f�[�^�T�C�Y
    /// </summary>
    private int MechDataSize = 4;

    /// <summary>
    /// ���[�^��1�f�[�^�T�C�Y
    /// </summary>
    private int MotorDataSize = 4;

    /// <summary>
    /// I/O��1�f�[�^�T�C�Y
    /// </summary>
    private int IoDataSize = 34;

    /// <summary>
    /// ��M�R�}���h
    /// </summary>
    Dictionary<int, List<ulong>> dctRcvDatas = new Dictionary<int, List<ulong>>();

    /// <summary>
    /// OS�o�[�W����
    /// </summary>
    private Version verOsVersion = new Version();

    /// <summary>
    /// OS�r���h
    /// </summary>
    private DateTime dtOsBuild = new DateTime();

    /// <summary>
    /// MICKS�o�[�W����
    /// </summary>
    private EnmMicksVer micksVer = EnmMicksVer.MICKS_VER1;

    /// <summary>��M�X���b�h</summary>
    private Thread threadReceive;

    /// <summary>��M�σt���O�F�X���b�h�ԒʐM�p</summary>
    private object responseSignal = new object();

    #endregion

    #region �ϐ�
    /// <summary>
    /// �R�}���hID
    /// </summary>
    private int _commandId = 0;
    #endregion �ϐ�
    /// <summary>
    /// �r�b�g���W�X�^��`
    /// </summary>
    protected override List<string> regTypeBit
    {
        get
        {
            return new List<string>(new string[] { "M", "X", "Y", "RX", "RY", "LM", "RWrB", "RWwB" });
        }
    }
    /// <summary>
    /// �r�b�g���W�X�^��`
    /// </summary>
    protected override List<string> regTypeBit16
    {
        get
        {
            return new List<string>(new string[] { "X", "Y", "RX", "RY", "RWrB", "RWwB" });
        }
    }

    /// <summary>
    /// 32bit���W�X�^��`
    /// </summary>
    protected override List<string> regTypeData32
    {
        get
        {
            return new List<string>(new string[] { "R", "A", "S", "P", "RWr", "RWw" });
        }
    }

    /// <summary>
    /// 64bit���W�X�^��`
    /// </summary>
    protected override List<string> regTypeData64
    {
        get
        {
            return new List<string>(new string[] { "G", "L", "GF", "LF" });
        }
    }

    /// <summary>
    /// �v���O�����ԍ������݂��郌�W�X�^��`
    /// </summary>
    protected override List<string> regTypeExistPrg
    {
        get
        {
            return new List<string>(new string[] { "L", "LF", "R", "A", "P", "LM" });
        }
    }

    /// <summary>
    /// �ꊇ��M�ݒ�
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 1000;
        }
    }

    /// <summary>
    /// �r�b�g��
    /// </summary>
    public override int BIT_COUNT
    {
        get
        {
            return 32;
        }
    }

    /// <summary>
    /// �J�n����
    /// </summary>
    protected override void Start()
    {
        base.Start();

        if (!GlobalScript.mickses.ContainsKey(Name))
        {
            GlobalScript.mickses.Add(Name, this);
        }
    }

    /// <summary>
    /// �ڑ�����
    /// </summary>
    /// <returns></returns>
    protected override bool Connect()
    {
        if (base.Connect())
        {
            // ����ʐM����
            int commandId = 0;
            if (Read(new KMXDBSetting { RegisterType = "S", RegisterNo = 1007, DataCount = 7, ProgramNo = 0 }, ref commandId))
            {
                var lstData = dctRcvDatas[commandId % 0x0F];
                uniLongAllData tmp1 = new uniLongAllData();
                uniLongAllData tmp2 = new uniLongAllData();
                tmp1.ulData = lstData[2];
                tmp2.ulData = lstData[3];
                dtOsBuild = new DateTime((int)(lstData[4] & 0xFFFFFFFF), (int)(lstData[5] & 0xFFFFFFFF), (int)(lstData[6] & 0xFFFFFFFF));
                if (dtOsBuild.Year >= 2014)
                {
                    micksVer = (EnmMicksVer)tmp1.bytData1;
                    verOsVersion = new Version(tmp1.bytData2.ToString() + "." + tmp1.ui16Data2.ToString() + "." + tmp2.ui32Data1.ToString());
                }
                else
                {
                    verOsVersion = new Version("1." + tmp1.ui16Data2.ToString() + "." + tmp2.ui32Data1.ToString());
                }
                // �o�C�i���ʐM�\�Ȃ烂�[�h�ύX
                if (verOsVersion >= new Version(1, 6, 0))
                {
                    // �o�C�i���ʐM���[�h
                    return EnableBinCom();
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// �d���쐬
    /// </summary>
    /// <param name="data"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    protected override List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong> values = null)
    {
        // ���W�X�^�R�[�h
        var regCode = (EnmRegCode)Enum.Parse(typeof(EnmRegCode), data.RegisterType);
        // �R�}���h
        var command = (values == null) ? EnmCommCommand.GetRegister : (regTypeBit.Contains(data.RegisterType) ? EnmCommCommand.SetIODeviceMultiBit : EnmCommCommand.SetRegister);
        // �T�u�R�}���h
        var subCommand = regTypeData64.Contains(data.RegisterType) ? eReadSubCommand.Device64 : eReadSubCommand.Device32;
        // �A�h���X
        var address = regTypeBit.Contains(data.RegisterType) ? (int)Math.Floor(data.RegisterNo / 32.0) : data.RegisterNo;
        // �f�[�^��
        var count = regTypeBit.Contains(data.RegisterType) ? (int)Math.Ceiling(data.AllDataCount / 32.0) : data.AllDataCount;
        // �f�[�^�o�C�g
        var dataByteMax = (values != null) ?  4 : ((address > 0xFFFF) || regTypeData64.Contains(data.RegisterType) ? 4 : 2);
        // ���M�f�[�^
        var datas = (values == null) ? new List<ulong> { (ulong)regCode, (ulong)data.ProgramNo, (ulong)address, (uint)count } : (regTypeBit.Contains(data.RegisterType) ? new List<ulong> { (ulong)regCode, (ulong)address, (ulong)values.Count } : new List<ulong> { (ulong)regCode, (ulong)data.ProgramNo, (ulong)address });
        if (values != null)
        {
            // �������݃f�[�^�Z�b�g
            if (regTypeBit.Contains(data.RegisterType))
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (i % 32 == 0)
                    {
                        // �ŏ��̃f�[�^
                        datas.Add(0);
                    }
                    if (values[i] == 1)
                    {
                        // ON
                        datas[datas.Count - 1] |= (ulong)1 << (i % 32);
                    }
                }
            }
            else
            {
                datas.AddRange(values);
            }
        }
        return CreateMessage(command, subCommand, dataByteMax, datas, ref commandId);
    }

    /// <summary>
    /// �d���쐬
    /// </summary>
    /// <param name="commCommand"></param>
    /// <param name="subCommand"></param>
    /// <param name="dataByteMax"></param>
    /// <param name="datas"></param>
    private List<byte> CreateMessage(EnmCommCommand command, eReadSubCommand subCommand, int dataByteMax, List<ulong> datas, ref int commandId)
    {
        var sendData = new List<byte>();
        // �R�}���hID���Z
        _commandId++;
        // �R�}���h�쐬
        string message =
            ((int)command).ToString("X4") +
            ((int)subCommand).ToString("X") +
            (_commandId & 0x0F).ToString("X") +
            datas.Count.ToString("X2") +
            dataByteMax.ToString("X");
        //�@�f�[�^�Z�b�g
        foreach (ulong dataN in datas)
        {
            var tmp = dataN.ToString($"X{dataByteMax * 2}");
            message += tmp.Substring(tmp.Length - dataByteMax * 2, dataByteMax * 2);
        }
        // BCC�쐬
        byte bcc = 0;
        foreach (byte tmp in message)
        {
            // �e�o�C�g�̔r���I�_���a
            bcc ^= tmp;
        }
        // ���M�f�[�^�쐬
        sendData.Add(STX);
        sendData.AddRange(Encoding.ASCII.GetBytes(message));
        sendData.Add(ETX);
        sendData.Add(bcc);
        sendData.Add(CR);
        commandId = _commandId;
        return sendData;
    }

    /// <summary>
    /// ��M�f�[�^����
    /// </summary>
    /// <param name="data"></param>
    /// <param name="datas"></param>
    /// <returns></returns>
    protected override bool AnalysysMessage(KMXDBSetting data, List<byte> datas)
    {
        var isBinary = (datas[0] == SYN);
        if (isBinary)
        {
        }
        else
        {
            // ETX�`�F�b�N
            var intDataFst = 0;
            var intDataEnd = 0;
            var intDataETX = datas.IndexOf(ETX);
            if (intDataETX > 0)
            {
                // CR�`�F�b�N
                intDataEnd = datas.IndexOf(CR);
            }
            // ETX�`�F�b�N
            if ((intDataETX > 0) && (intDataEnd < 0))
            {
                // �T���`�F�b�N��0�̂Ƃ�
                intDataEnd = intDataETX + 2;
            }
            else if (intDataETX + 1 == intDataEnd)
            {
                // �T���`�F�b�N��\r�̂Ƃ�
                intDataEnd++;
            }
            if (intDataEnd > intDataFst)
            {
                // �f�[�^����
                datas.RemoveRange(intDataEnd, datas.Count - intDataEnd);
                var strRcvData = Encoding.UTF8.GetString(datas.ToArray());
                var rcvData = new ClsComFormat();
                ReceiveHead(ref rcvData, strRcvData);
                if (!rcvData.isError)
                {
                    ReceiveData(ref rcvData, strRcvData);
                    dctRcvDatas[(int)rcvData.bytCmdId] = new List<ulong>();
                    if (regTypeBit.Contains(data.RegisterType))
                    {
                        //�r�b�g���ƂɃf�[�^���擾
                        for (var i = 0; i < (int)rcvData.intDataNum * BIT_COUNT; i++)
                        {
                            int shift = i % 32;
                            int index = i / 32;
                            var value = (rcvData.lstData[index].ulData >> shift) & 1;
                            dctRcvDatas[(int)rcvData.bytCmdId].Add(value);
                            if (i < data.values.Count)
                            {
                                data.values[i].Value = (int)value;
                            }
                        }
                    }
                    else
                    {
                        dctRcvDatas[(int)rcvData.bytCmdId].AddRange(rcvData.lstData.Select(d => d.ulData));
                        for (int i = 0; i < (int)rcvData.intDataNum; i++)
                        {
                            if (i < data.values.Count)
                            {
                                data.values[i].Value = rcvData.lstData[i].int32Data1;
                            }
                        }
                    }
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// ��M�f�[�^����w�b�_���쐬
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveHead(ref ClsComFormat clsRcv, string strRcvData)
    {
        // �R�}���h
        clsRcv.strCmdCode = strRcvData.Substring(COM_CMD_IDX, COM_CMD_LEN);
        int.TryParse(clsRcv.strCmdCode, NumberStyles.HexNumber, null, out clsRcv.intCmdCode);
        // �T�u�R�}���h
        clsRcv.strSubCmd = strRcvData.Substring(COM_SUBCMD_IDX, COM_SUBCMD_LEN);
        byte.TryParse(clsRcv.strSubCmd, NumberStyles.HexNumber, null, out clsRcv.bytSubCmd);
        // �R�}���hID
        clsRcv.strCmdId = strRcvData.Substring(COM_CMDID_IDX, COM_CMDID_LEN);
        byte.TryParse(clsRcv.strCmdId, NumberStyles.HexNumber, null, out clsRcv.bytCmdId);
        // �f�[�^����
        clsRcv.strDataNum = strRcvData.Substring(COM_DATANUM_IDX, COM_DATANUM_LEN);
        int.TryParse(clsRcv.strDataNum, NumberStyles.HexNumber, null, out clsRcv.intDataNum);
        // �f�[�^�o�C�g�T�C�Y
        clsRcv.strDataSize = strRcvData.Substring(COM_DATASIZE_IDX, COM_DATASIZE_LEN);
        byte.TryParse(clsRcv.strDataSize, NumberStyles.HexNumber, null, out clsRcv.bytDataSize);
        clsRcv.isError = ((byte)strRcvData[0] != ACK);
    }

    #region �@�\�ݒ�R�}���h
    /// <summary>�@�\������</summary>
    /// <param name="intMechNo">�@�\No</param>
    /// <returns>���� or ���s</returns>
    public bool EnableBinCom()
    {
        // ���M�t�H�[�}�b�g�쐬
        List<ulong> lstPrm = new List<ulong>();
        lstPrm.Add(1);
        int commandId = 0;
        var message = CreateMessage(EnmCommCommand.FuncBinCom, 0, 4, lstPrm, ref commandId);
        if (message.Count > 0)
        {
            // �f�[�^���M����
            var buff = SendCommand(message);
            if (buff.Count > 2)
            {
                // ��M�f�[�^���͏���
                return true;
            }
        }
        return false;
    }
    #endregion �@�\�ݒ�R�}���h


    #region �v���g�R�����
    /// <summary>
    /// ��M�f�[�^����f�[�^���쐬
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveData(ref ClsComFormat clsRcv, string strRcvData)
    {
        int offset = clsRcv.IsLargeCapacity ? COM_DATA_IDX_EX : COM_DATA_IDX;
        // �f�[�^�N���A
        clsRcv.lstData.Clear();
        int i;
        for (i = 0; i < clsRcv.intDataNum; i++)
        {
            string strData = strRcvData.Substring(offset + i * clsRcv.bytDataSize * 2, clsRcv.bytDataSize * 2);
            uniLongAllData uniAllData = new uniLongAllData();
            long.TryParse(strData, NumberStyles.HexNumber, null, out uniAllData.lngData);
            clsRcv.lstData.Add(uniAllData);
        }
    }

    /// <summary>
    /// ��M�f�[�^����w�b�_���쐬
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveHead(ref ClsComFormat clsRcv, byte[] data)
    {
        // �R�}���h
        clsRcv.intCmdCode = (int)data[COM_CMD_IDX_BIN] + ((int)data[COM_CMD_IDX_BIN + 1] << 8);
        clsRcv.strCmdCode = clsRcv.intCmdCode.ToString("X4");
        // �T�u�R�}���h
        clsRcv.bytSubCmd = (byte)(data[COM_SUBCMD_IDX_BIN] & 0x0F);
        clsRcv.strSubCmd = clsRcv.bytSubCmd.ToString("X1");
        // �R�}���hID
        clsRcv.bytCmdId = (byte)((data[COM_SUBCMD_IDX_BIN] >> 4) & 0x0F);
        clsRcv.strCmdId = clsRcv.bytCmdId.ToString("X1");
        // �f�[�^����
        clsRcv.intDataNum = (int)data[COM_DATANUM_IDX_BIN] + ((int)data[COM_DATANUM_IDX_BIN + 1] << 8);
        clsRcv.strDataNum = clsRcv.intDataNum.ToString("X4");
        // �f�[�^�T�C�Y
        clsRcv.bytDataSize = (byte)data[COM_DATASIZE_IDX_BIN];
        clsRcv.strDataSize = clsRcv.bytDataSize.ToString("X1");
    }

    /// <summary>
    /// ��M�f�[�^����f�[�^���쐬
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveData(ref ClsComFormat clsRcv, byte[] data)
    {
        // �f�[�^�N���A
        clsRcv.lstData.Clear();
        for (int i = 0; i < clsRcv.intDataNum; i++)
        {
            uniLongAllData uniAllData = new uniLongAllData();
            uniAllData.bytData1 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 0];
            uniAllData.bytData2 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 1];
            if (clsRcv.bytDataSize >= 4)
            {
                uniAllData.bytData3 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 2];
                uniAllData.bytData4 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 3];
            }
            if (clsRcv.bytDataSize == 8)
            {
                uniAllData.bytData5 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 4];
                uniAllData.bytData6 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 5];
                uniAllData.bytData7 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 6];
                uniAllData.bytData8 = (byte)data[COM_DATA_IDX_BIN + i * clsRcv.bytDataSize + 7];
            }
            clsRcv.lstData.Add(uniAllData);
        }
    }
    #endregion �v���g�R�����
}
