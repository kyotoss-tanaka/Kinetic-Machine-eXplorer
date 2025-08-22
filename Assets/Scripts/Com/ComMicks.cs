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

    #region クラス
    /// <summary>コマンドフォーマット</summary>
    public class ClsComFormat
    {
        /// <summary>コマンドコード</summary>
        public string strCmdCode;
        public int intCmdCode;
        /// <summary>サブコマンド</summary
        public string strSubCmd;
        public byte bytSubCmd;
        /// <summary>コマンドID</summary>
        public string strCmdId;
        public byte bytCmdId;
        /// <summary>データ数</summary>
        public string strDataNum;
        public int intDataNum;
        /// <summary>データ長</summary>
        public string strDataSize;
        public byte bytDataSize;
        /// <summary>データ</summary>
        public List<uniLongAllData> lstData = new List<uniLongAllData>();
        /// <summary>文字列データ 8000番以降の特殊コマンド用</summary>
        public string strData;
        /// <summary>CFアクセスコマンド</summary>
        public bool blnCFCommand;
        /// <summary>タイムアウト時間</summary>
        public int intTimeOut = 0;
        /// <summary>エラー状態</summary>
        public bool isError = false;
        /// <summary>大容量通信</summary>
        public bool IsLargeCapacity = false;
    }
    #endregion クラス

    #region 列挙
    /// <summary>MICKSバージョン</summary>
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
    /// サブコマンド
    /// </summary>
    private enum eReadSubCommand
    {
        /// <summary>
        /// 32bitデバイス、特殊レジスタ
        /// </summary>
        Device32,
        /// <summary>
        /// 64bitG、L、GF、LFレジスタ
        /// </summary>
        Device64,
        /// <summary>
        /// 64bitG、Lレジスタデバイスコード取得
        /// </summary>
        DeviceCode
    }

    /// <summary>
    /// コマンド種別
    /// </summary>
    public enum EnmCommCommand
    {
        //*********************
        // 通常コマンド
        //*********************
        /// <summary>レジスタ設定</summary>
        SetRegister = 0x100,
        /// <summary>レジスタ取得</summary>
        GetRegister = 0x101,
        /// <summary>I/Oデバイス１ビット出力</summary>
        SetIODeviceBit = 0x102,
        /// <summary>I/Oデバイスマルチビット出力</summary>
        SetIODeviceMultiBit = 0x103,

        /// <summary>実POSパラメータを取得</summary>
        GetActivatedPosParam = 0x160,

        /// <summary>機構の登録</summary>
        SetMecha = 0x200,
        /// <summary>機構の取得</summary>
        GetMecha = 0x201,
        /// <summary>機構登録エリアの取得</summary>
        GetMechArea = 0x202,

        /// <summary>全機構位置情報一括取得</summary>
        GetAllMechPos = 0x250,

        /// <summary>アンプパラメータ設定 SSC-NETⅢ</summary>
        SetAmpParameterNet3 = 0x300,
        /// <summary>アンプパラメータ取得 SSC-NETⅢ</summary>
        GetAmpParameterNet3 = 0x301,
        /// <summary>アンプパラメータ設定 MECHATOROLINKⅢ</summary>
        SetAmpParameterMech3 = 0x310,
        /// <summary>アンプパラメータ取得 MECHATOROLINKⅢ</summary>
        GetAmpParameterMech3 = 0x311,
        /// <summary>アンプパラメータ設定 SSC-NETⅢ</summary>
        SetAmpParameterMC120 = 0x320,
        /// <summary>アンプパラメータ取得 SSC-NETⅢ</summary>
        GetAmpParameterMC120 = 0x321,
        /// <summary>アンプパラメータ設定 SSC-NETⅢ</summary>
        SetAmpParameterMC210 = 0x322,
        /// <summary>アンプパラメータ取得 SSC-NETⅢ</summary>
        GetAmpParameterMC210 = 0x323,
        /// <summary>アンプパラメータ設定 SSC-NETⅢ</summary>
        SetAmpParameterMga023 = 0x324,
        /// <summary>アンプパラメータ取得 SSC-NETⅢ</summary>
        GetAmpParameterMga023 = 0x325,
        /// <summary>アンプパラメータ設定 EtherCAT</summary>
        SetAmpParameterEcat = 0x326,
        /// <summary>アンプパラメータ取得 EtherCAT</summary>
        GetAmpParameterEcat = 0x327,

        /// <summary>プログラム操作</summary>
        OperateProgram = 0x400,
        /// <summary>ブレークポイント設定</summary>
        SetBreakPoint = 0x401,
        /// <summary>プログラムアラーム受信</summary>
        GetProgramAlm = 0x402,
        /// <summary>プログラムアラームコード受信</summary>
        GetProgramAlmCode = 0x403,
        /// <summary>プログラム待ちI/O受信</summary>
        GetProgramWaitIo = 0x404,
        /// <summary>プログラム行数取得</summary>
        SetProgramLineNum = 0x405,
        /// <summary>プログラムトレースI/Oセット</summary>
        SetProgramTraceTrg = 0x406,

        /// <summary>オブジェクトグループ情報取得</summary>
        GetObjectGroupInfo = 0x500,
        /// <summary>オブジェクト情報取得</summary>
        GetObjectInfo = 0x501,
        /// <summary>オブジェクト範囲情報取得</summary>
        GetObjectRangeInfo = 0x502,
        /// <summary>オブジェクト登録情報取得</summary>
        GetObjectRegInfo = 0x503,
        /// <summary>オブジェクトサブ範囲情報取得</summary>
        GetObjectSubRangeInfo = 0x504,
        /// <summary>オブジェクトサブI/O範囲情報取得</summary>
        GetObjectSubIoRangeInfo = 0x505,
        /// <summary>オブジェクト登録情報取得(RAM10個Ver)</summary>
        GetObjectRegInfoEx = 0x506,

        /// <summary>差動情報取得</summary>
        GetDiffRegInfo = 0x540,

        /// <summary>カムポジ情報取得</summary>
        GetCamPosInfo = 0x550,
        /// <summary>カムポジ情報取得(拡張版)</summary>
        GetCamPosInfoEx = 0x551,

        /// <summary>エンコーダ同期情報取得</summary>
        GetEncSyncInfo = 0x560,
        /// <summary>エンコーダ同期作業テーブル情報取得</summary>
        GetEncSyncWorkTable = 0x561,
        /// <summary>エンコーダ同期テーブル情報取得</summary>
        GetEncSyncEncSyncTable = 0x562,
        /// <summary>エンコーダ同期テーブルデータ取得</summary>
        GetEncSyncEncSyncData = 0x563,
        /// <summary>エンコーダ同期登録状態取得</summary>
        GetEncSyncRegInfo = 0x564,

        /// <summary>カメラ情報取得</summary>
        GetCameraInfo = 0x570,
        /// <summary>カメラデータ取得</summary>
        GetCameraData,
        /// <summary>カメラトリガセット</summary>
        SetCameraTrg,
        /// <summary>カメラデータクリア</summary>
        ClrCameraData,
        /// <summary>カメラ全データクリア</summary>
        ClrCameraAllData,

        /// <summary>キャリブレーション情報取得</summary>
        GetGlblGetCalibInfo = 0x580,
        /// <summary>グローバル座標データ取得</summary>
        GetGlblGetCdntData,
        /// <summary>グローバル座標情報設定</summary>
        SetGlblGetCdntData,
        /// <summary>キャリブレーション操作</summary>
        SetGlblCalibOpt,

        /// <summary>I/Oフィルター情報取得</summary>
        GetIoFilterInfo = 0x590,

        /// <summary>アラーム履歴取得</summary>
        GetAlarmLog = 0x600,
        /// <summary>ログ履歴取得</summary>
        GetMicksLog,
        /// <summary>ABS情報取得</summary>
        GetAbsInfo,
        /// <summary>SRAMABS情報取得</summary>
        GetSramAbsInfo,
        /// <summary>SRAMデータ取得</summary>
        GetSramData,

        /// <summary>メモリ取得</summary>
        GetMemory = 0x650,
        /// <summary>PCIメモリ取得</summary>
        GetPciMemory,
        /// <summary>CC-Linkメモリ取得</summary>
        GetCCLinkMemory,
        /// <summary>SSCNETメモリ取得</summary>
        GetSscnetMemory,
        /// <summary>サーボユニットメモリ取得</summary>
        GetServoUnitMemory,
        /// <summary>サーボエラー情報取得</summary>
        GetServoErrInfo,
        /// <summary>メモリ設定</summary>
        SetMemory = 0x660,

        /// <summary>測定データセット前処理</summary>
        SetMeasureDataPre = 0x700,
        /// <summary>測定データセット後処理</summary>
        SetMeasureDataAfter = 0x701,
        /// <summary>測定対象設定</summary>
        SetMeasureObject = 0x702,
        /// <summary>測定対象取得</summary>
        GetMeasureObject = 0x703,
        /// <summary>測定データトリガ設定</summary>
        SetMeasureTrigger = 0x704,
        /// <summary>測定データトリガ取得</summary>
        GetMeasureTrigger = 0x705,
        /// <summary>測定データ取得</summary>
        GetMeasureData = 0x706,
        /// <summary>測定状態の取得</summary>
        GetMeasureCondition = 0x707,
        /// <summary>測定の中止</summary>
        StopMeasure = 0x708,

        /// <summary>タイムチャート設定取得</summary>
        GetTimeChartSetting = 0x720,
        /// <summary>タイムチャートデータ取得</summary>
        GetTimeChartData = 0x721,

        // リアルタイムログデータ取得
        GetRtLogData = 0x725,
        /// <summary>リアルタイムログ最新データ取得</summary>
        GetRtLogLatestData,

        /// <summary>1μsec当たりのCPUカウント取得</summary>
        GetCpu1usecCount = 0x730,
        /// <summary>I/O履歴データ取得</summary>
        GetIoHistoryData = 0x731,

        /// <summary>タイムスタンプ開始</summary>
        StartTimeStamp = 0x750,
        /// <summary>タイムスタンプ中止</summary>
        StopTimeStamp,
        /// <summary>タイムスタンプ状態取得</summary>
        GetTimeStampInfo,
        /// <summary>タイムスタンプデータ取得</summary>
        GetTimeStampData,

        /// <summary>ドライブレコーダ情報取得</summary>
        GetDrvrecInfo = 0x760,
        /// <summary>ドライブレコーダデータ取得</summary>
        GetDrvrecData,
        /// <summary>ドライブレコーダパラメータ取得</summary>
        GetDrvrecPrmData,

        /// <summary>システム時間の設定</summary>
        SetSystemTime = 0x800,
        /// <summary>積算時間のクリア</summary>
        ClearAddTime,

        /// <summary>パラメータCF書込</summary>
        WriteCFParameter = 0x900,
        /// <summary>プログラムCF書込</summary>
        WriteCFProgram = 0x901,

        /// <summary>OSファイルアップロード</summary>
        OSFileUpLoad = 0xA00,
        /// <summary>システムテスト</summary>
        SystemTest = 0xA01,

        /// <summary>データ履歴を取得</summary>
        GetDataHistoryData = 0xB00,
        /// <summary>データ履歴ポイントを取得</summary>
        GetDataHistoryPoint = 0xB01,

        //*****************************************
        // デバッグコマンド 0x7000～
        //*****************************************
        /// <summary>機構初期化</summary>
        MechInit = 0x7000,
        /// <summary>原点セット</summary>
        SetOrg = 0x7001,
        /// <summary>サーボOFF</summary>
        ServoOff = 0x7002,
        /// <summary>機構停止</summary>
        MechStop = 0x7003,
        /// <summary>機構減速停止</summary>
        MechSlowDown = 0x7004,
        //*****************************************
        // 機能設定コマンド 0x7800～
        //*****************************************
        /// <summary>バイナリ通信</summary>
        FuncBinCom = 0x7800,
        //*****************************************
        // 特殊コマンド 0x8000～
        //*****************************************
        /// <summary>プログラム送信</summary>
        SetProgram = 0x8000,
        /// <summary>プログラム受信</summary>
        GetProgram = 0x8001,
    }


    /// <summary>レジスタコード</summary>
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
        /// <summary>60 ～ 99:予約</summary>
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
    /// Rレジスタ定義
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
    /// Aレジスタ定義
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

    #region 定数
    // パケット内情報
    /*
    /// <summary>コマンドインデックス</summary>
    private int COM_CMD_IDX = 1;
    /// <summary>コマンド長</summary>
    private int COM_CMD_LEN = 4;
    /// <summary>サブコマンドインデックス</summary>
    private int COM_SUBCMD_IDX = 5;
    /// <summary>サブコマンド長</summary>
    private int COM_SUBCMD_LEN = 1;
    /// <summary>コマンドIDインデックス</summary>
    private int COM_CMDID_IDX = 6;
    /// <summary>コマンドID長</summary>
    private int COM_CMDID_LEN = 1;
    /// <summary>データ数インデックス</summary>
    private int COM_DATANUM_IDX = 7;
    /// <summary>データ数長</summary>
    private int COM_DATANUM_LEN = 2;
    /// <summary>データサイズインデックス</summary>
    private int COM_DATASIZE_IDX = 9;
    /// <summary>データサイズ長</summary>
    private int COM_DATASIZE_LEN = 1;
    /// <summary>データインデックス</summary>
    private int COM_DATA_IDX = 10;
    */
    /// <summary>コマンドインデックス</summary>
    private const int COM_CMD_IDX = 1;
    /// <summary>コマンド長</summary>
    private const int COM_CMD_LEN = 4;
    /// <summary>サブコマンドインデックス</summary>
    private int COM_SUBCMD_IDX { get { return COM_CMD_IDX + COM_CMD_LEN; } }
    /// <summary>サブコマンド長</summary>
    private const int COM_SUBCMD_LEN = 1;
    /// <summary>コマンドIDインデックス</summary>
    private int COM_CMDID_IDX { get { return COM_SUBCMD_IDX + COM_SUBCMD_LEN; } }
    /// <summary>コマンドID長</summary>
    private const int COM_CMDID_LEN = 1;
    /// <summary>データ数インデックス</summary>
    private int COM_DATANUM_IDX { get { return COM_CMDID_IDX + COM_CMDID_LEN; } }
    /// <summary>データ数長</summary>
    private const int COM_DATANUM_LEN = 2;
    /// <summary>データサイズインデックス</summary>
    private int COM_DATASIZE_IDX { get { return COM_DATANUM_IDX + COM_DATANUM_LEN; } }
    /// <summary>データサイズ長</summary>
    private const int COM_DATASIZE_LEN = 1;
    /// <summary>データインデックス</summary>
    private int COM_DATA_IDX { get { return COM_DATASIZE_IDX + COM_DATASIZE_LEN; } }

    /// <summary>データ数長</summary>
    private const int COM_DATANUM_LEN_EX = 4;
    /// <summary>データサイズインデックス</summary>
    private int COM_DATASIZE_IDX_EX { get { return COM_DATANUM_IDX + COM_DATANUM_LEN_EX; } }
    /// <summary>データインデックス</summary>
    private int COM_DATA_IDX_EX { get { return COM_DATASIZE_IDX_EX + COM_DATASIZE_LEN; } }

    /// <summary>コマンドインデックス</summary>
    private const int COM_CMD_IDX_BIN = 1;
    /// <summary>コマンド長</summary>
    private const int COM_CMD_LEN_BIN = 2;
    /// <summary>サブコマンドインデックス</summary>
    private int COM_SUBCMD_IDX_BIN { get { return COM_CMD_IDX_BIN + COM_CMD_LEN_BIN; } }
    /// <summary>サブコマンド長</summary>
    private const int COM_SUBCMD_LEN_BIN = 1;
    /// <summary>データ数インデックス</summary>
    private int COM_DATANUM_IDX_BIN { get { return COM_SUBCMD_IDX_BIN + COM_SUBCMD_LEN_BIN; } }
    /// <summary>データ数長</summary>
    private const int COM_DATANUM_LEN_BIN = 2;
    /// <summary>データサイズインデックス</summary>
    private int COM_DATASIZE_IDX_BIN { get { return COM_DATANUM_IDX_BIN + COM_DATANUM_LEN_BIN; } }
    /// <summary>データサイズ長</summary>
    private const int COM_DATASIZE_LEN_BIN = 1;
    /// <summary>データインデックス</summary>
    private int COM_DATA_IDX_BIN { get { return COM_DATASIZE_IDX_BIN + COM_DATASIZE_LEN_BIN; } }

    /// <summary>
    /// 機構最大数
    /// </summary>
    public static int MechMax = 64;
    /// <summary>
    /// 特殊レジスタ最大数
    /// </summary>
    public static int SpRegMax = 100;
    /// <summary>
    /// Lレジスタ最大数
    /// </summary>
    public static int LRegMax = 2000;
    /// <summary>
    /// データ件数オフセット
    /// </summary>
    private static int DataCountOffset = 6;
    /// <summary>
    /// データバイト最大数オフセット
    /// </summary>
    private static int DataByteMaxOffset = 8;
    /// <summary>
    /// データオフセット
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
    /// タイムチャートデータ最大
    /// </summary>
    private int TimeChartDataMax = 5000;

    /// <summary>
    /// 機構の1データサイズ
    /// </summary>
    private int MechDataSize = 4;

    /// <summary>
    /// モータの1データサイズ
    /// </summary>
    private int MotorDataSize = 4;

    /// <summary>
    /// I/Oの1データサイズ
    /// </summary>
    private int IoDataSize = 34;

    /// <summary>
    /// 受信コマンド
    /// </summary>
    Dictionary<int, List<ulong>> dctRcvDatas = new Dictionary<int, List<ulong>>();

    /// <summary>
    /// OSバージョン
    /// </summary>
    private Version verOsVersion = new Version();

    /// <summary>
    /// OSビルド
    /// </summary>
    private DateTime dtOsBuild = new DateTime();

    /// <summary>
    /// MICKSバージョン
    /// </summary>
    private EnmMicksVer micksVer = EnmMicksVer.MICKS_VER1;

    /// <summary>受信スレッド</summary>
    private Thread threadReceive;

    /// <summary>受信済フラグ：スレッド間通信用</summary>
    private object responseSignal = new object();

    #endregion

    #region 変数
    /// <summary>
    /// コマンドID
    /// </summary>
    private int _commandId = 0;
    #endregion 変数
    /// <summary>
    /// ビットレジスタ定義
    /// </summary>
    protected override List<string> regTypeBit
    {
        get
        {
            return new List<string>(new string[] { "M", "X", "Y", "RX", "RY", "LM", "RWrB", "RWwB" });
        }
    }
    /// <summary>
    /// ビットレジスタ定義
    /// </summary>
    protected override List<string> regTypeBit16
    {
        get
        {
            return new List<string>(new string[] { "X", "Y", "RX", "RY", "RWrB", "RWwB" });
        }
    }

    /// <summary>
    /// 32bitレジスタ定義
    /// </summary>
    protected override List<string> regTypeData32
    {
        get
        {
            return new List<string>(new string[] { "R", "A", "S", "P", "RWr", "RWw" });
        }
    }

    /// <summary>
    /// 64bitレジスタ定義
    /// </summary>
    protected override List<string> regTypeData64
    {
        get
        {
            return new List<string>(new string[] { "G", "L", "GF", "LF" });
        }
    }

    /// <summary>
    /// プログラム番号が存在するレジスタ定義
    /// </summary>
    protected override List<string> regTypeExistPrg
    {
        get
        {
            return new List<string>(new string[] { "L", "LF", "R", "A", "P", "LM" });
        }
    }

    /// <summary>
    /// 一括受信設定
    /// </summary>
    public override int BULK_RCV_COUNT
    {
        get
        {
            return 1000;
        }
    }

    /// <summary>
    /// ビット数
    /// </summary>
    public override int BIT_COUNT
    {
        get
        {
            return 32;
        }
    }

    /// <summary>
    /// 開始処理
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
    /// 接続処理
    /// </summary>
    /// <returns></returns>
    protected override bool Connect()
    {
        if (base.Connect())
        {
            // 初回通信処理
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
                // バイナリ通信可能ならモード変更
                if (verOsVersion >= new Version(1, 6, 0))
                {
                    // バイナリ通信モード
                    return EnableBinCom();
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 電文作成
    /// </summary>
    /// <param name="data"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    protected override List<byte> CreateMessage(KMXDBSetting data, ref int commandId, List<ulong> values = null)
    {
        // レジスタコード
        var regCode = (EnmRegCode)Enum.Parse(typeof(EnmRegCode), data.RegisterType);
        // コマンド
        var command = (values == null) ? EnmCommCommand.GetRegister : (regTypeBit.Contains(data.RegisterType) ? EnmCommCommand.SetIODeviceMultiBit : EnmCommCommand.SetRegister);
        // サブコマンド
        var subCommand = regTypeData64.Contains(data.RegisterType) ? eReadSubCommand.Device64 : eReadSubCommand.Device32;
        // アドレス
        var address = regTypeBit.Contains(data.RegisterType) ? (int)Math.Floor(data.RegisterNo / 32.0) : data.RegisterNo;
        // データ数
        var count = regTypeBit.Contains(data.RegisterType) ? (int)Math.Ceiling(data.AllDataCount / 32.0) : data.AllDataCount;
        // データバイト
        var dataByteMax = (values != null) ?  4 : ((address > 0xFFFF) || regTypeData64.Contains(data.RegisterType) ? 4 : 2);
        // 送信データ
        var datas = (values == null) ? new List<ulong> { (ulong)regCode, (ulong)data.ProgramNo, (ulong)address, (uint)count } : (regTypeBit.Contains(data.RegisterType) ? new List<ulong> { (ulong)regCode, (ulong)address, (ulong)values.Count } : new List<ulong> { (ulong)regCode, (ulong)data.ProgramNo, (ulong)address });
        if (values != null)
        {
            // 書き込みデータセット
            if (regTypeBit.Contains(data.RegisterType))
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (i % 32 == 0)
                    {
                        // 最初のデータ
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
    /// 電文作成
    /// </summary>
    /// <param name="commCommand"></param>
    /// <param name="subCommand"></param>
    /// <param name="dataByteMax"></param>
    /// <param name="datas"></param>
    private List<byte> CreateMessage(EnmCommCommand command, eReadSubCommand subCommand, int dataByteMax, List<ulong> datas, ref int commandId)
    {
        var sendData = new List<byte>();
        // コマンドID加算
        _commandId++;
        // コマンド作成
        string message =
            ((int)command).ToString("X4") +
            ((int)subCommand).ToString("X") +
            (_commandId & 0x0F).ToString("X") +
            datas.Count.ToString("X2") +
            dataByteMax.ToString("X");
        //　データセット
        foreach (ulong dataN in datas)
        {
            var tmp = dataN.ToString($"X{dataByteMax * 2}");
            message += tmp.Substring(tmp.Length - dataByteMax * 2, dataByteMax * 2);
        }
        // BCC作成
        byte bcc = 0;
        foreach (byte tmp in message)
        {
            // 各バイトの排他的論理和
            bcc ^= tmp;
        }
        // 送信データ作成
        sendData.Add(STX);
        sendData.AddRange(Encoding.ASCII.GetBytes(message));
        sendData.Add(ETX);
        sendData.Add(bcc);
        sendData.Add(CR);
        commandId = _commandId;
        return sendData;
    }

    /// <summary>
    /// 受信データ分析
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
            // ETXチェック
            var intDataFst = 0;
            var intDataEnd = 0;
            var intDataETX = datas.IndexOf(ETX);
            if (intDataETX > 0)
            {
                // CRチェック
                intDataEnd = datas.IndexOf(CR);
            }
            // ETXチェック
            if ((intDataETX > 0) && (intDataEnd < 0))
            {
                // サムチェックが0のとき
                intDataEnd = intDataETX + 2;
            }
            else if (intDataETX + 1 == intDataEnd)
            {
                // サムチェックが\rのとき
                intDataEnd++;
            }
            if (intDataEnd > intDataFst)
            {
                // データ正常
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
                        //ビットごとにデータを取得
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
    /// 受信データからヘッダを作成
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveHead(ref ClsComFormat clsRcv, string strRcvData)
    {
        // コマンド
        clsRcv.strCmdCode = strRcvData.Substring(COM_CMD_IDX, COM_CMD_LEN);
        int.TryParse(clsRcv.strCmdCode, NumberStyles.HexNumber, null, out clsRcv.intCmdCode);
        // サブコマンド
        clsRcv.strSubCmd = strRcvData.Substring(COM_SUBCMD_IDX, COM_SUBCMD_LEN);
        byte.TryParse(clsRcv.strSubCmd, NumberStyles.HexNumber, null, out clsRcv.bytSubCmd);
        // コマンドID
        clsRcv.strCmdId = strRcvData.Substring(COM_CMDID_IDX, COM_CMDID_LEN);
        byte.TryParse(clsRcv.strCmdId, NumberStyles.HexNumber, null, out clsRcv.bytCmdId);
        // データ件数
        clsRcv.strDataNum = strRcvData.Substring(COM_DATANUM_IDX, COM_DATANUM_LEN);
        int.TryParse(clsRcv.strDataNum, NumberStyles.HexNumber, null, out clsRcv.intDataNum);
        // データバイトサイズ
        clsRcv.strDataSize = strRcvData.Substring(COM_DATASIZE_IDX, COM_DATASIZE_LEN);
        byte.TryParse(clsRcv.strDataSize, NumberStyles.HexNumber, null, out clsRcv.bytDataSize);
        clsRcv.isError = ((byte)strRcvData[0] != ACK);
    }

    #region 機能設定コマンド
    /// <summary>機構初期化</summary>
    /// <param name="intMechNo">機構No</param>
    /// <returns>成功 or 失敗</returns>
    public bool EnableBinCom()
    {
        // 送信フォーマット作成
        List<ulong> lstPrm = new List<ulong>();
        lstPrm.Add(1);
        int commandId = 0;
        var message = CreateMessage(EnmCommCommand.FuncBinCom, 0, 4, lstPrm, ref commandId);
        if (message.Count > 0)
        {
            // データ送信処理
            var buff = SendCommand(message);
            if (buff.Count > 2)
            {
                // 受信データ分析処理
                return true;
            }
        }
        return false;
    }
    #endregion 機能設定コマンド


    #region プロトコル解析
    /// <summary>
    /// 受信データからデータを作成
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveData(ref ClsComFormat clsRcv, string strRcvData)
    {
        int offset = clsRcv.IsLargeCapacity ? COM_DATA_IDX_EX : COM_DATA_IDX;
        // データクリア
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
    /// 受信データからヘッダを作成
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveHead(ref ClsComFormat clsRcv, byte[] data)
    {
        // コマンド
        clsRcv.intCmdCode = (int)data[COM_CMD_IDX_BIN] + ((int)data[COM_CMD_IDX_BIN + 1] << 8);
        clsRcv.strCmdCode = clsRcv.intCmdCode.ToString("X4");
        // サブコマンド
        clsRcv.bytSubCmd = (byte)(data[COM_SUBCMD_IDX_BIN] & 0x0F);
        clsRcv.strSubCmd = clsRcv.bytSubCmd.ToString("X1");
        // コマンドID
        clsRcv.bytCmdId = (byte)((data[COM_SUBCMD_IDX_BIN] >> 4) & 0x0F);
        clsRcv.strCmdId = clsRcv.bytCmdId.ToString("X1");
        // データ件数
        clsRcv.intDataNum = (int)data[COM_DATANUM_IDX_BIN] + ((int)data[COM_DATANUM_IDX_BIN + 1] << 8);
        clsRcv.strDataNum = clsRcv.intDataNum.ToString("X4");
        // データサイズ
        clsRcv.bytDataSize = (byte)data[COM_DATASIZE_IDX_BIN];
        clsRcv.strDataSize = clsRcv.bytDataSize.ToString("X1");
    }

    /// <summary>
    /// 受信データからデータを作成
    /// </summary>
    /// <param name="clsRcvData"></param>
    /// <param name="strRcvData"></param>
    private void ReceiveData(ref ClsComFormat clsRcv, byte[] data)
    {
        // データクリア
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
    #endregion プロトコル解析
}
