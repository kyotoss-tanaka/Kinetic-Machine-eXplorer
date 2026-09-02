using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using UnityEngine;
using static Parameters.DBSetting;

namespace Parameters
{
    public enum RobotType
    {
        /// <summary>
        /// 2軸アーム
        /// </summary>
        ARM,
        /// <summary>
        /// 天吊り2軸アーム
        /// </summary>
        CEILING_ARM,
        /// <summary>
        /// 村田パラレル(3軸)
        /// </summary>
        MPS2_3AS,
        /// <summary>
        /// 村田パラレル(4軸)
        /// </summary>
        MPS2_4AS,
        /// <summary>
        /// 変則パラレル
        /// </summary>
        MPX_PI,
        /// <summary>
        /// MPX-R2
        /// </summary>
        MPX_R2,
        /// <summary>
        /// MPX-R3
        /// </summary>
        MPX_R3,
        /// <summary>
        /// MPX-R3S
        /// </summary>
        MPX_R3S,
        /// <summary>
        /// MPX-R6
        /// </summary>
        MPX_R6,
        /// <summary>
        /// 川重パラレル
        /// </summary>
        YF03N4,
        /// <summary>
        /// 川重6軸
        /// </summary>
        RS007L,
        /// <summary>
        /// FANUC CRX-30iA
        /// </summary>
        CRX_30iA,
        /// <summary>
        /// FANUC M-20iD25
        /// </summary>
        M_20iD25,
        /// <summary>
        /// 未定義
        /// </summary>
        UNDEFINED
    }

    [Serializable]
    public class PostgresSetting
    {
        public class KmxDirectData
        {
            public string mechId { get; set; } = "";
            public eProtocolType protocol { get; set; } = eProtocolType.None;
            public string IpAddress { get; set; } = "";
            public int PortNo { get; set; }
            public int NetAddress { get; set; }
            public int PcNo { get; set; }
            public string endpointURL { get; set; } = "";
            public string nameSpaceIndex { get; set; } = "";
            public bool ethernetIpIsLarge { get; set; }
            public int ethernetIpLargeSize { get; set; }
            public List<KMXDBSetting> tags { get; set; } = new();
            public bool isMcProtocol
            {
                get
                {
                    return protocol == eProtocolType.McProtocol || protocol == eProtocolType.McProtocol_UDP;
                }
            }
            public bool isMicks
            {
                get
                {
                    return protocol == eProtocolType.MICKS;
                }
            }
            public bool isUdp
            {
                get
                {
                    return protocol == eProtocolType.McProtocol_UDP;
                }
            }
            public bool isOpcUa
            {
                get
                {
                    return protocol == eProtocolType.OPC_UA;
                }
            }
            public bool isEtherNetIP
            {
                get
                {
                    return protocol == eProtocolType.EtherNetIP;
                }
            }
        }

        public int No { get; set; }
        public int Type { get; set; }
        public int Cycle { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public int ClientMode { get; set; }
        public int DirectMode { get; set; }
        public string EndpointUrl { get; set; }
        public int ns { get; set; }
        public List<KmxDirectData> directDatas { get; set; } = new();
        public string Name
        {
            get
            {
                return Server + ":" + Port;
            }
        }
        public bool isClientMode
        {
            get
            {
                return ClientMode == 1;
            }
        }
        public bool isDirectMode
        {
            get
            {
                return DirectMode == 1;
            }
        }
        public bool isPostgres
        {
            get
            {
                return Type == 0 && !isDirectMode;
            }
        }
        public bool isMongo
        {
            get
            {
                return Type == 1 && !isDirectMode;
            }
        }
        public bool isMqtt
        {
            get
            {
                return Type == 2 && !isDirectMode;
            }
        }
        public bool isRedis
        {
            get
            {
                return Type == 3 && !isDirectMode;
            }
        }
        public bool isInner
        {
            get
            {
                return Type == 10;
            }
        }
    }

    [Serializable]
    public class DataExchangeSetting
    {
        /// <summary>
        /// DB番号
        /// </summary>
        public int dbNo { get; set; }
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// データ設定
        /// </summary>
        public List<DataEx> datas { get; set; }
    }

    public class DataEx
    {
        /// <summary>
        /// 初期値
        /// </summary>
        public int initValue { get; set; }
        /// <summary>
        /// 入力データ
        /// </summary>
        public string input { get; set; }
        /// <summary>
        /// 出力データ
        /// </summary>
        public string output { get; set; }
        /// <summary>
        /// 初期処理フラグ
        /// </summary>
        public bool isInit
        {
            get
            {
                return (input == null) || (input == "");
            }
        }
    }

    [Serializable]
    public class UnitSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string Database;
        /// <summary>
        /// DB番号
        /// </summary>
        public int dbNo { get; set; }
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 衝突あり
        /// </summary>
        public int collision { get; set; }
        /// <summary>
        /// 同期機構
        /// </summary>
        public bool sync { get; set; }
        /// <summary>
        /// グループオブジェクト
        /// </summary>
        public string group { get; set; }
        /// <summary>
        /// 親オブジェクト
        /// </summary>
        public string parent { get; set; }
        /// <summary>
        /// 絶対パス
        /// </summary>
        public string path { get; set; }
        /// <summary>
        /// ロボットタイムチャートモード
        /// </summary>
        public bool isRoboTimeChart { get; set; }
        /// <summary>
        /// 子オブジェクト名
        /// </summary>
        public List<UnitChildren> children { get; set; }
        /// <summary>
        /// 子オブジェクト
        /// </summary>
        public List<GameObject> childrenObject;
        /// <summary>
        /// 動作設定
        /// </summary>
        public UnitActionSetting actionSetting;
        /// <summary>
        /// ロボット設定
        /// </summary>
        [SerializeReference]
        public RobotSetting robotSetting;
        /// <summary>
        /// ロボット設定
        /// </summary>
        [SerializeReference]
        public LinearSetting linearSetting;
        /// <summary>
        /// 型替え部品設定
        /// </summary>
        public ChangeOverSetting changeOverSetting;
        /// <summary>
        /// ワーク生成設定
        /// </summary>
        public List<WorkCreateSetting> workSettings;
        /// <summary>
        /// ワーク生成設定
        /// </summary>
        public List<WorkDeleteSetting> workDeleteSettings;
        /// <summary>
        /// ワーク受渡設定
        /// </summary>
        public List<WorkTransferSetting> workTransferSettings;
        /// <summary>
        /// センサ設定
        /// </summary>
        public List<SensorSetting> sensorSettings;
        /// <summary>
        /// 吸引設定
        /// </summary>
        public SuctionSetting suctionSetting;
        /// <summary>
        /// 物体形状設定
        /// </summary>
        public ShapeSetting shapeSetting;
        public SafetyZoneSetting safetyZoneSetting;   // DCS安全ゾーン（可視化・読むだけ）
        /// <summary>
        /// スイッチ設定
        /// </summary>
        public SwitchSetting switchSetting;
        /// <summary>
        /// シグナルタワー設定
        /// </summary>
        public SignalTowerSetting towerSetting;
        /// <summary>
        /// LED設定
        /// </summary>
        public LedSetting ledSetting;
        /// <summary>
        /// 拡張機構設定
        /// </summary>
        public ExMechSetting exMechSetting;
        /// <summary>
        /// バケット設定
        /// </summary>
        public BacketSetting backetSetting;
        /// <summary>
        /// 動作オブジェクト
        /// </summary>
        public GameObject moveObject = null;
        /// <summary>
        /// ユニットオブジェクト
        /// </summary>
        public GameObject unitObject { get; set; }
        /// <summary>
        /// 衝突あり
        /// </summary>
        public bool isCollision
        {
            get
            {
                return collision == 1;
            }
        }
    }

    [Serializable]
    public class UnitChildren
    {
        public string name { get; set; }
        public string group { get; set; }
        public string path { get; set; }
        public bool isUnit { get; set; }
        public GameObject childObject = null;
    }

    [Serializable]
    public class UnitActionSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 動作モード 0:直線 1:回転 2:外部(直線) 3:外部(回転)
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// 動作軸 0:X 1:Y 2:Z
        /// </summary>
        public int axis { get; set; }
        /// <summary>
        /// 回転方向
        /// </summary>
        public int dir { get; set; }
        /// <summary>
        /// オフセット
        /// </summary>
        public int offset { get; set; }
        /// <summary>
        /// 加速度設定 0:加速度(G) 1:時間
        /// </summary>
        public int acl { get; set; }
        /// <summary>
        /// 動作タグ
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 動作タグ倍率
        /// </summary>
        public int rate { get; set; }
        /// <summary>
        /// 通信遅れ時間
        /// </summary>
        public int delay { get; set; }
        /// <summary>
        /// サイクル時間
        /// </summary>0
        public int cycle { get; set; }
        /// <summary>
        /// 拡張機構モード変更
        /// </summary>
        public bool exModeChange { get; set; }
        /// <summary>
        /// 動作ファイル
        /// </summary>
        public bool file { get; set; }
        /// <summary>
        /// 動作設定
        /// </summary>
        public List<UnitAction> actions { get; set; } = new();

        public bool isInternal
        {
            get
            {
                return (mode == 0 || mode == 1) && !file;
            }
        }
        public bool isExternal
        {
            get
            {
                return (mode == 2 || mode == 3) && !file;
            }
        }
        public bool isActionTable
        {
            get
            {
                return file;
            }
        }
        public bool isRobo
        {
            get
            {
                return mode == 4;
            }
        }
        public bool isLinear
        {
            get
            {
                return mode == 5;
            }
        }
        public bool isPlanarMotor
        {
            get
            {
                return mode == 6;
            }
        }
        public bool isConveyer
        {
            get
            {
                return mode == 7;
            }
        }
        public bool isChangeOver
        {
            get
            {
                return mode == 8;
            }
        }
    }

    [Serializable]
    public class UnitAction
    {
        /// <summary>
        /// トリガタイミング
        /// </summary>
        public int trg { get; set; }
        /// <summary>
        /// 目標位置
        /// </summary>
        public float target { get; set; }
        /// <summary>
        /// オフセット
        /// </summary>
        public float offset { get; set; }
        /// <summary>
        /// 方向
        /// </summary>
        public int dir { get; set; }
        /// <summary>
        /// ストローク
        /// </summary>
        public float stroke { get; set; }
        /// <summary>
        /// 動作時間
        /// </summary>
        public float time { get; set; }
        /// <summary>
        /// 加速設定
        /// </summary>
        public float acl { get; set; }
        /// <summary>
        /// 減速設定
        /// </summary>
        public float dcl { get; set; }
        /// <summary>
        /// 開始トリガI/O
        /// </summary>
        public string start { get; set; }
        /// <summary>
        /// 完了I/O
        /// </summary>
        public string end { get; set; }
        /// <summary>
        /// 開始名
        /// </summary>
        public string startName { get; set; }
        /// <summary>
        /// 完了名
        /// </summary>
        public string endName { get; set; } 
        /// <summary>
        /// 継続フラグ
        /// </summary>
        public bool isContinue { get; set; }
        /// <summary>
        /// 継続動作時の停止時間
        /// </summary>
        public float stop { get; set; }
        /// <summary>
        /// 目標座標
        /// </summary>
        [JsonIgnore]
        public Vector3 targetPos { get; set; }
        /// <summary>
        /// 速度
        /// </summary>
        [JsonIgnore]
        public float velocity { get; set; }
        /// <summary>
        /// 加速時間
        /// </summary>
        [JsonIgnore]
        public float aclTime { get; set; }
        /// <summary>
        /// 減速時間
        /// </summary>
        [JsonIgnore]
        public float dclTime { get; set; }
        /// <summary>
        /// 加速度
        /// </summary>
        [JsonIgnore]
        public float aclVal { get; set; }
        /// <summary>
        /// 減速度
        /// </summary>
        [JsonIgnore]
        public float dclVal { get; set; }
        /// <summary>
        /// データ変更フラグ
        /// </summary>
        [JsonIgnore]
        public bool isChanged { get; set; }
    }

    [Serializable]
    public class HiddenUnit
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 親名
        /// </summary>
        public string parent { get; set; }
        /// <summary>
        /// モード
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// 無効フラグ
        /// </summary>
        public int disable { get; set; }
        /// <summary>
        /// 有効
        /// </summary>
        public bool isEnable
        {
            get
            {
                return disable == 0;
            }
        }

    }

    [Serializable]
    public class InnerProcessSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// サイクル
        /// </summary>
        public decimal cycle { get; set; }
        /// <summary>
        /// ONタイミング
        /// </summary>
        public decimal onTiming { get; set; }
        /// <summary>
        /// OFFタイミング
        /// </summary>
        public decimal offTiming { get; set; }
    }

    /// <summary>
    /// 内部デバイス（仮想I/O）。実PLC/DBに無い、シミュレータ内部専用の名前付きI/O信号。
    /// スイッチ出力(SwitchInfo.tag)とタイムチャート(ActionInfo start/end)を同一信号で内部結線するのに使う。
    /// 通信モードに依存せず tagDatas に事前確保する（kmx_ros2 側 内部デバイス仕様書）。
    /// </summary>
    [Serializable]
    public class InternalDeviceSetting
    {
        /// <summary>機番</summary>
        public string mechId { get; set; }
        /// <summary>内部デバイス名（基底名）</summary>
        public string name { get; set; }
        /// <summary>配列数（1で単一、2以上で配列）</summary>
        public int count { get; set; }
        /// <summary>true=ビット / false=ワード</summary>
        public bool isBit { get; set; }
        /// <summary>備考</summary>
        public string remark { get; set; }
        /// <summary>展開済みタグ名（ActionInfo/SwitchInfo の start/end/tag と完全一致。配列表記はツール側確定済み）</summary>
        public List<string> tags { get; set; } = new();
    }

    [Serializable]
    public class ChuckUnitSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// チャックユニット名
        /// </summary>
        public List<ChuckUnit> children { get; set; }

    }

    [Serializable]
    public class ChuckUnit
    {
        public string name { get; set; }
        public int offset { get; set; }
        public float rate { get; set; }
        public int dir { get; set; }
        [JsonIgnore]
        public UnitSetting setting { get; set; }
        [JsonIgnore]
        public Vector3 startPos { get; set; }
    }

    /// <summary>
    /// ROS2 経路計画の1ステップ（ロボをタイムチャートに載せる）。RobotSetting.robotSteps に列で持つ。
    /// start タグ ON でこのステップ開始 → poseDeg(関節角) へ MoveIt 計画/再生 → 到達で end タグに 1。
    /// KMX Tool で編集し RobotInfo.json に格納。
    /// </summary>
    [Serializable]
    public class Ros2RobotStep
    {
        /// <summary>表示名（任意）</summary>
        public string name { get; set; } = "";
        /// <summary>開始タグ（ON でこのステップ開始）</summary>
        public string start { get; set; } = "";
        /// <summary>終了タグ（到達で 1 を書く）</summary>
        public string end { get; set; } = "";
        /// <summary>再生時間(秒)。>0 なら軌道をこの時間へ再スケール（0 は軌道の元時間）</summary>
        public float time { get; set; }
        /// <summary>終了姿勢＝関節角 J1..Jn(度)</summary>
        public List<float> poseDeg { get; set; } = new();
    }

    [Serializable]
    public class RobotSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ロボットタイプ
        /// </summary>
        public string type { get; set; }
        /// <summary>
        /// ヘッドユニット
        /// </summary>
        public string head { get; set; }
        /// <summary>
        /// チャックユニット名
        /// </summary>
        public List<string> tags { get; set; }
        /// <summary>
        /// 倍率
        /// </summary>
        public List<int> rates { get; set; }
        /// <summary>
        /// オフセット
        /// </summary>
        public List<float> offset { get; set; }
        /// <summary>
        /// タイムチャート使用
        /// </summary>
        public bool isTm { get; set; }
        /// <summary>
        /// タイムチャートユニット
        /// </summary>
        public List<string> tmUnitNames { get; set; } = new();
        /// <summary>
        /// ROS2 経路計画ステップ列（開始タグ→終了姿勢→終了タグ）。KMX Tool で編集・RobotInfo.json に格納。
        /// </summary>
        public List<Ros2RobotStep> robotSteps { get; set; } = new();
        /// <summary>
        /// ヘッドユニット設定
        /// </summary>
        public UnitSetting headUnit { get; set; }
        /// <summary>
        /// ヘッドユニット設定
        /// </summary>
        public List<UnitSetting> tmUnits { get; set; } = new();
        /// <summary>
        /// コントローラIP
        /// </summary>
        public string robotIp {get;set;} = "127.0.0.1";
        /*
        /// <summary>
        /// ロボットタイプ
        /// </summary>
        public RobotType robo
        {
            get
            {
                switch (type)
                {
                    case "MPS2-3AS":
                        return RobotType.MPS2_3AS;

                    case "MPS2-4AS":
                        return RobotType.MPS2_4AS;

                    case "MPX-PI":
                        return RobotType.MPX_PI;

                    case "MPX-R2":
                        return RobotType.MPX_R2;

                    case "MPX-R3":
                        return RobotType.MPX_R3;

                    case "YF03N4":
                        return RobotType.YF03N4;

                    case "RS007L":
                        return RobotType.RS007L;

                    default:
                        return RobotType.UNDEFINED;

                }
            }
        }
        */
    }

    [Serializable]
    public class LinearSetting
    {
        [Serializable]
        public class PointInfo
        {
            public string name { get; set; }
            public float pos { get; set; }
            public string tagAct { get; set; }
            public string tagProcess { get; set; }
            public string tagFin { get; set; }
            public string type { get; set; }
            public int spd { get; set; }
            public int count { get; set; }
            public int wait { get; set; }
            public int pitch { get; set; }
        }
        [Serializable]
        public class SpdInfo
        {
            public float vm { get; set; } = 1000;
            public float vf { get; set; } = 0;
            public float ve { get; set; } = 0;
            public float acl { get; set; } = 1;
            public float dcl { get; set; } = 1;
            public float jerkA { get; set; }
            public float jerkD { get; set; }
        }
        public string mechId { get; set; }
        public string name { get; set; }
        public string model { get; set; }
        public string group { get; set; }
        public string path { get; set; }
        public string type { get; set; }
        public float length { get; set; }
        public int count { get; set; }
        public float pitch { get; set; }
        public float offset { get; set; }
        public bool stat { get; set; }
        public int org { get; set; }
        public bool rvs { get; set; }
        public List<float> statPos { get; set; }
        public List<PointInfo> points { get; set; }
        public List<SpdInfo> spds { get; set; }
        [JsonIgnore]
        public GameObject gameObject { get; set; }
    }

    [Serializable]
    public class PlanarMotorSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 計算式
        /// </summary>
        public string calc { get; set; }
        /// <summary>
        /// リニア数
        /// </summary>
        public int count { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> offset_p { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> offset_r { get; set; }
        /// <summary>
        /// 方向(位置)
        /// </summary>
        public List<int> dir_p { get; set; }
        /// <summary>
        /// 方向(角度)
        /// </summary>
        public List<int> dir_r { get; set; }
        /// <summary>
        /// 位置タグ名
        /// </summary>
        public List<string> tags_p { get; set; }
        /// <summary>
        /// 角度タグ名
        /// </summary>
        public List<string> tags_r { get; set; }
        /// <summary>
        /// 倍率(位置)
        /// </summary>
        public List<int> rate_p { get; set; }
        /// <summary>
        /// 倍率(角度)
        /// </summary>
        public List<int> rate_r { get; set; }
        /// <summary>
        /// ヘッドユニット
        /// </summary>
        public string mover { get; set; }
        /// <summary>
        /// ヘッドユニット設定
        /// </summary>
        public UnitSetting moverUnit { get; set; }
    }


    /// <summary>
    /// コンベア設定（ロジック搬送方式）。距離系はm、速度はm/sec、加速度はm/sec²。
    /// 搬送面は物体形状設定（あれば優先）またはベルト面モデルの境界から自動算出する。
    /// </summary>
    [Serializable]
    public class ConveyerSetting
    {
        /// <summary>
        /// 速度行（上から評価し最初にONのタグの速度で動作。全OFFで停止）
        /// </summary>
        [Serializable]
        public class SpeedData
        {
            /// <summary>動作タグ</summary>
            public string tag { get; set; } = "";
            /// <summary>速度(m/sec)</summary>
            public float spd { get; set; }
        }

        /// <summary>
        /// ストッパー/整列ガイド
        /// </summary>
        [Serializable]
        public class BlockerData
        {
            public string model { get; set; } = "";
            public string group { get; set; } = "";
            public string path { get; set; } = "";
            /// <summary>0=ストッパー 1=整列ガイド</summary>
            public int role { get; set; }
            /// <summary>接触面オフセット(m)。+で接触面をワーク側へ広げる</summary>
            public float offset { get; set; }
            [JsonIgnore]
            public GameObject gameObject { get; set; }
        }

        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 動作軸 0:X 1:Y 2:Z（ユニットローカル）
        /// </summary>
        public int axis { get; set; }
        /// <summary>
        /// 方向 +1/-1
        /// </summary>
        public int dir { get; set; }
        /// <summary>
        /// 加速度(m/sec²)。0=瞬時
        /// </summary>
        public float acl { get; set; }
        /// <summary>
        /// 速度テーブル
        /// </summary>
        public List<SpeedData> speeds { get; set; } = new();
        /// <summary>
        /// ベルト面モデルのパス（物体形状設定があればそちら優先）
        /// </summary>
        public string beltPath { get; set; } = "";
        /// <summary>
        /// ワーク間ギャップ(m)
        /// </summary>
        public float gap { get; set; }
        /// <summary>
        /// 搬送面の高さ許容(m)
        /// </summary>
        public float margin { get; set; }
        /// <summary>
        /// 搬送面の高さ補正(m)。自動算出面（境界上面）への加算。複数コンベアの面高さ合わせ用
        /// </summary>
        public float surface { get; set; }
        /// <summary>
        /// 終端動作 0=そのまま 1=物理落下
        /// </summary>
        public int endMode { get; set; }
        /// <summary>
        /// ストッパー/整列ガイド
        /// </summary>
        public List<BlockerData> blockers { get; set; } = new();
        /// <summary>
        /// ベルト面モデル（実行時解決）
        /// </summary>
        [JsonIgnore]
        public GameObject beltObject { get; set; }
    }

    [Serializable]
    public class WorkCreateSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ワーク名
        /// </summary>
        public string work { get; set; }
        /// <summary>
        /// 設計位置を使用（ワークモデル設定の設計配置位置で生成する）
        /// </summary>
        public bool isDesignPos { get; set; }
        /// <summary>
        /// 把持可能
        /// </summary>
        public int grabbable { get; set; }
        /// <summary>
        /// タイマー
        /// </summary>
        public int timer { get; set; }
        /// <summary>
        /// 生成サイクル
        /// </summary>
        public float cycle { get; set; }
        /// <summary>
        /// 生成タグ
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 生存距離
        /// </summary>
        public float alive { get; set; }
        /// <summary>
        /// バケット番号
        /// </summary>
        public int backetno { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> rot { get; set; }
        /// <summary>
        /// 重力使用
        /// </summary>
        public bool gravity { get; set; }
        /// <summary>
        /// 変更
        /// </summary>
        public bool change { get; set; }
        /// <summary>
        /// 動作無視
        /// </summary>
        public bool ignoreMove { get; set; }
        /// <summary>
        /// 触れないように
        /// </summary>
        public bool isTouch { get; set; } = true;
        public bool isGrabbable
        {
            get
            {
                return grabbable == 1;
            }
        }
        public bool isTimer
        {
            get
            {
                return timer == 1;
            }
        }
    }

    [Serializable]
    public class WorkDeleteSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 削除対象ワーク名（空欄=全ワーク）
        /// </summary>
        public string work { get; set; } = "";
        /// <summary>
        /// タグ名
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 距離
        /// </summary>
        public float distance { get; set; }
        /// <summary>
        /// バケット番号
        /// </summary>
        public int backetno { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>バケット削除の発動位置（ワールド）。ロード時に経路（バケット番号×ピッチ＋オフセット）から算出される</summary>
        [JsonIgnore]
        public Vector3 fixedWorldPos { get; set; }
        /// <summary>fixedWorldPosが算出済みか（バケット削除のみtrue）</summary>
        [JsonIgnore]
        public bool isFixedPos { get; set; }
    }

    [Serializable]
    public class WorkTransferSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// モード（0=アタッチ、1=変換）
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// タグ名（アタッチ=ON中保持/OFFで解放、変換=立ち上がりで実行）
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 対象ワーク名（空欄=範囲内の全ワーク）
        /// </summary>
        public string work { get; set; } = "";
        /// <summary>
        /// 変換先ワーク名（変換モードのみ）
        /// </summary>
        public string workTo { get; set; } = "";
        /// <summary>
        /// 対象範囲の中心（動作部基準）
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// 対象範囲の距離
        /// </summary>
        public float range { get; set; }
    }

    [Serializable]
    public class WorkModelSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ワーク名（識別名）
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 設計配置モデルのシーンパス
        /// </summary>
        public string path { get; set; }
    }

    [Serializable]
    public class SensorSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// センサ生成
        /// </summary>
        public int create { get; set; }
        /// <summary>
        /// 幅
        /// </summary>
        public float width { get; set; }
        /// <summary>
        /// 生成タグ
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> rot { get; set; }

        public bool isCreate
        {
            get
            {
                return create == 1;
            }
        }
    }

    [Serializable]
    public class SuctionSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 生成タグ
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 出力タグ
        /// </summary>
        public string tag_output { get; set; }
        /// <summary>
        /// 固定(位置)
        /// </summary>
        public List<int> pos_fixed { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<int> rot_fixed { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> rot { get; set; }
    }

    [Serializable]
    public class ShapeSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public bool auto { get; set; }
        /// <summary>
        /// 形
        /// </summary>
        public List<UnitShape> datas { get; set; }
    }

    [Serializable]
    public class UnitShape
    {
        /// <summary>
        /// 自動設定
        /// </summary>
        public bool create { get; set; }
        /// <summary>
        /// 中心点
        /// </summary>
        public List<float> center { get; set; }
        /// <summary>
        /// サイズ
        /// </summary>
        public List<float> size { get; set; }
    }

    /// <summary>
    /// DCS(Dual Check Safety)安全ゾーン設定（SafetyZoneInfo.json）。ロボットunitに紐づく。
    /// DCSのカルテシアン安全ゾーン(直交箱)を KMX に取り込んで可視化するための読み取り専用データ。
    /// 詳細: kmx_ros2/DCS_ZONE_IMPORT_SPEC.md。
    /// </summary>
    [Serializable]
    public class SafetyZoneSetting
    {
        public string mechId { get; set; }              // どのロボットunitか（shapeSetting と同じ結線キー。JSON運用）
        public string name { get; set; }                // ユニット名（結線キー）／表示名
        public string robotId { get; set; }             // ROS受信時の robot_id（[[MULTI_ROBOT_ROS2_SPEC]]）。JSONでは通常空
        public string frame { get; set; }               // "world"（ロボットWorld/base）等。DCS定義フレーム
        public string unit { get; set; }                // "mm"（既定）。KMX側で ×0.001
        public List<float> calibrationEuler { get; set; }   // 座標合わせ(度・任意)。空なら既定(0,-90,0)。§4.4 実測調整用
        public List<float> calibrationOffset { get; set; }  // 座標合わせ(m・任意)。空なら(0,0,0)
        public List<SafetyZone> zones { get; set; }
    }

    [Serializable]
    public class SafetyZone
    {
        public string id { get; set; }              // 例 "CPC1"
        public bool enabled { get; set; }
        public bool insideAllowed { get; set; }     // true=箱の内側が安全域(居てよい・緑)/false=箱の内側が進入禁止(赤)
        public List<float> min { get; set; }        // [xmin,ymin,zmin]（frame・unit準拠）
        public List<float> max { get; set; }        // [xmax,ymax,zmax]
    }

    [Serializable]
    public class ExMechSetting
    {
        public string mechId { get; set; }
        public string name { get; set; }
        public int type { get; set; }
        public List<ExMechModel> datas { get; set; }
        /// <summary>主軸（動作部モデル）の子モデル（回転中心指定用。旧データはnull）</summary>
        public ExMechModel main { get; set; }
    }

    [Serializable]
    public class ExMechModel : ExMechChildren
    {
        public List<ExMechChildren> children { get; set; } = new();
    }

    [Serializable]
    public class ExMechChildren
    {
        public string model { get; set; } = "";
        public string group { get; set; } = "";
        public string path { get; set; } = "";
        /// <summary>0=通常、1=回転中心（バウンズ中心が親モデルの回転中心。モデルは親に追従）、2=回転中心(固定)（中心参照のみ。親子付け替えせず据え置き）</summary>
        public int type { get; set; }
        [JsonIgnore]
        public GameObject gameObject { get; set; }
        [JsonIgnore]
        public bool isChild { get; set; }
    }

    [Serializable]
    public class BacketSetting
    {
        /// <summary>
        /// 経路要素（ループ順。2件以上あればベルトモデルより優先して経路を自動生成）
        /// </summary>
        [Serializable]
        public class PathElement
        {
            /// <summary>0=スプロケット、1=経由点</summary>
            public int type { get; set; }
            public string path { get; set; } = "";
            /// <summary>半径オフセット(m)。スプロケット=モデル検出半径への補正、経由点=角丸め半径(0=そのまま通過)</summary>
            public float offset { get; set; }
            /// <summary>モデル未指定時の座標(m、動作部モデル基準、KMX座標系X,Y,Z)</summary>
            public float[] pos { get; set; } = new float[3];
            [JsonIgnore]
            public GameObject gameObject { get; set; }
        }

        public string mechId { get; set; } = "";
        public string name { get; set; } = "";
        public string model { get; set; } = "";
        public string group { get; set; } = "";
        public string path { get; set; } = "";
        public int count { get; set; }
        public float pitch { get; set; }
        public float offset { get; set; }
        /// <summary>周長(mm)。ロード時に経路設定(PathInfo)から充填される（0=経路から算出）</summary>
        [JsonIgnore]
        public float loopLength { get; set; }
        /// <summary>周長と経路長の差を経路上に均等配分する。ロード時に経路設定(PathInfo)から充填される</summary>
        [JsonIgnore]
        public bool loopScaling { get; set; }
        /// <summary>経路の開始位置オフセット(m)。ロード時に経路設定(PathInfo)から充填される</summary>
        [JsonIgnore]
        public float pathStartOffset { get; set; }
        /// <summary>逆回り（ループの進行方向を反転）。ロード時に経路設定(PathInfo)から充填される</summary>
        [JsonIgnore]
        public bool pathReverse { get; set; }
        public bool visible { get; set; }
        /// <summary>常に上向き（吊り下げ式。経路を循環しても姿勢を変えない）</summary>
        public bool upright { get; set; }
        /// <summary>参照する経路名（PathInfo.jsonの経路。指定時はベルトモデルより優先）</summary>
        public string pathName { get; set; } = "";
        /// <summary>経路要素（ロード時にpathNameから解決して充填される）</summary>
        public List<PathElement> pathElements { get; set; } = new();
        [JsonIgnore]
        public GameObject gameObject { get; set; }
    }

    /// <summary>
    /// 経路設定（PathInfo.json。機番ごとの名前付き循環経路）
    /// </summary>
    [Serializable]
    public class PathInfoSetting
    {
        public string mechId { get; set; } = "";
        /// <summary>経路名（バケット設定などからの参照キー）</summary>
        public string name { get; set; } = "";
        /// <summary>周長(mm)。この距離の移動でちょうど1周する（0=経路から算出）</summary>
        public float loopLength { get; set; }
        /// <summary>周長と経路長の差を経路上に均等配分する（false=経路長を超えた時点で先頭へ戻る）</summary>
        public bool loopScaling { get; set; }
        /// <summary>開始位置オフセット(m)。経路の開始位置を進行方向にずらす（参照する全ユニットに効く）</summary>
        public float startOffset { get; set; }
        /// <summary>逆回り（ループの進行方向を反転）</summary>
        public bool reverse { get; set; }
        public List<BacketSetting.PathElement> elements { get; set; } = new();
    }

    [Serializable]
    public class SwitchSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 色
        /// </summary>
        public string color { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// オルタネイト
        /// </summary>
        public bool alternate { get; set; }
        /// <summary>
        /// 初期値
        /// </summary>
        public bool value { get; set; }
        /// <summary>
        /// モード
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> rot { get; set; }
    }

    [Serializable]
    public class SignalTowerSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// タワータイプ
        /// </summary>
        public int type { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string red { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string yellow { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string green { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string blue { get; set; }
        /// <summary>
        /// タグ名
        /// </summary>
        public string white { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> rot { get; set; }
    }

    [Serializable]
    public class LedSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// タワータイプ
        /// </summary>
        public int type { get; set; }
        /// <summary>
        /// タグデータ
        /// </summary>
        public List<LedTagData> ledDatas { get; set; } = new();
    }

    [Serializable]
    public class PrefabSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; }
    }

    [Serializable]
    public class LedTagData
    {
        /// <summary>
        /// 色
        /// </summary>
        public string color { get; set; }
        /// <summary>
        /// タグ
        /// </summary>
        public string tag { get; set; }
    }

    [Serializable]
    public class CardboardSetting
    {
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// ユニット名
        /// </summary>
        public string name { get; set; } = "";
        /// <summary>
        /// サイクル
        /// </summary>
        public int cycle { get; set; }
        /// <summary>
        /// サイクル
        /// </summary>
        public int cycle2 { get; set; }
        /// <summary>
        /// モード
        /// </summary>
        public int mode { get; set; }
        /// <summary>
        /// L1
        /// </summary>
        public string l1_Body { get; set; } = "";
        /// <summary>
        /// L1上フラップ
        /// </summary>
        public string l1_Top { get; set; } = "";
        /// <summary>
        /// L1下フラップ
        /// </summary>
        public string l1_Bottom { get; set; } = "";
        /// <summary>
        /// L2
        /// </summary>
        public string l2_Body { get; set; } = "";
        /// <summary>
        /// L2上フラップ
        /// </summary>
        public string l2_Top { get; set; } = "";
        /// <summary>
        /// L2下フラップ
        /// </summary>
        public string l2_Bottom { get; set; } = "";
        /// <summary>
        /// W1
        /// </summary>
        public string w1_Body { get; set; } = "";
        /// <summary>
        /// W1上フラップ
        /// </summary>
        public string w1_Top { get; set; } = "";
        /// <summary>
        /// W1下フラップ
        /// </summary>
        public string w1_Bottom { get; set; } = "";
        /// <summary>
        /// We
        /// </summary>
        public string w2_Body { get; set; } = "";
        /// <summary>
        /// W2上フラップ
        /// </summary>
        public string w2_Top { get; set; } = "";
        /// <summary>
        /// W2下フラップ
        /// </summary>
        public string w2_Bottom { get; set; } = "";
    }

    /// <summary>
    /// 型替え設定
    /// </summary>
    [Serializable]

    public class ChangeOverSetting
    {
        [Serializable]
        public class ChangeOverPos
        {
            public int value { get; set; }
            public List<float> pos { get; set; } = new();
            public List<float> rot { get; set; } = new();
        }
        public string mechId { get; set; } = "";
        public string name { get; set; } = "";
        public string tag { get; set; } = "";
        public List<float> pos { get; set; } = new();
        public List<float> rot { get; set; } = new();
        public bool isChange { get; set; }
        public List<ChangeOverPos> datas { get; set; } = new();
    }

    [Serializable]
    public class DebugSetting
    {
        /// <summary>
        /// データベース
        /// </summary>
        public string database { get; set; }
        /// <summary>
        /// 機番
        /// </summary>
        public string mechId { get; set; }
        /// <summary>
        /// 折り返し用入力タグ
        /// </summary>
        public string input { get; set; }
        /// <summary>
        /// 折り返し用出力タグ
        /// </summary>
        public string output { get; set; }
        /// <summary>
        /// カウンタ用入力タグ
        /// </summary>
        public string inputCnt { get; set; }
        /// <summary>
        /// カウンタ用出力タグ
        /// </summary>
        public string outputCnt { get; set; }
        /// <summary>
        /// サイクルタグ
        /// </summary>
        public string cycle { get; set; }
    }

    [Serializable]
    public class BuildConfig
    {
        public string name { get; set; }
        public string mechId { get; set; }
        public bool isRelease { get; set; }
        public bool isVR { get; set; }
        public bool isMR { get; set; }
        /// <summary>
        /// 表示言語（"auto"=OS言語に追従 / "ja" / "en"）
        /// </summary>
        public string language { get; set; } = "auto";
        public bool isMaster { get; set; }
        public bool isCollision { get; set; }
        public bool isXR { get { return isVR || isMR; } }
    }

    /// <summary>WebGL 専用設定（StreamingAssets/Datas/WebGlSetting.json）。無ければ既定値。</summary>
    [Serializable]
    public class WebGlSetting
    {
        /// <summary>実行時の目標フレームレート(fps)。WebGLは単一スレッドで描画も重いので抑制すると安定。
        /// 0以下=抑制しない(既定120)。例: 30 / 15。実機WebGLのみ適用。</summary>
        public int targetFrameRate { get; set; } = 30;
        /// <summary>ロード中の目標フレームレート(fps)。低くすると重いシーンの毎フレーム描画が抑えられ、
        /// 単一スレッドのロード処理にCPUが回って**ロードが大幅に速くなる**（実測）。既定1。0以下=抑制しない。</summary>
        public int loadFrameRate { get; set; } = 1;
    }

    [Serializable]
    public class ActionData
    {
        public decimal time { get; set; }
        public decimal value { get; set; }
    }

    [Serializable]
    public class ActionTableData
    {
        public string mechId { get; set; } = "";
        public string name { get; set; } = "";
        public List<ActionData> datas { get; set; } = new();
    }

    [Serializable]
    public class DeciceArea
    {
        public string dev { get; set; } = "";
        public string tag { get; set; } = "";
        public string name { get; set; } = "";
        public int no { get; set; }
        public int count { get; set; }
        public int size { get; set; }
    }

    [Serializable]
    public class UseDeviceData
    {
        public string mechId { get; set; } = "";
        public List<DeciceArea> devices { get; set; } = new();
    }

    /// <summary>
    /// hmx-link（デジタルツイン WebSocket）設定。StreamingAssets/Datas/HmxLink.json
    /// </summary>
    /// <summary>
    /// hmx-link subscribe の connection（§5）。HMI と同時接続する場合に必須。
    /// HMI プロジェクトの projectSettings.connections[0] と同一値にすること（異なると HMI の PLC 接続を巻き込む）。
    /// </summary>
    [Serializable]
    public class HmxConnection
    {
        public string host { get; set; } = "";
        public int port { get; set; } = 0;
        public string protocol { get; set; } = "";
        public string transport { get; set; } = "";
    }

    /// <summary>
    /// タッチ操作の感度倍率（HmxLink.json の "touch" で起動時設定）。
    /// 1.0=既定、小さいほど鈍く（動きすぎを抑える）。
    /// </summary>
    [Serializable]
    public class TouchSetting
    {
        /// <summary>1本指ドラッグ=カメラ回転 の感度倍率</summary>
        public float orbit { get; set; } = 1.0f;
        /// <summary>2本指ドラッグ=パン の感度倍率</summary>
        public float pan { get; set; } = 1.0f;
        /// <summary>ピンチ=ズーム の感度倍率</summary>
        public float pinch { get; set; } = 1.0f;
    }

    [Serializable]
    public class HmxLinkSetting
    {
        /// <summary>有効化（true で ComHmi が ComInner/native PLC の代わりに動く）</summary>
        public bool enabled { get; set; } = false;
        /// <summary>hmx-link の WebSocket URL（例: ws://localhost:8765）</summary>
        public string wsUrl { get; set; } = "ws://localhost:8765";
        /// <summary>希望配信周期(ms)</summary>
        public int interval { get; set; } = 200;
        /// <summary>PLC接続設定。host が空なら subscribe に含めない（HMI同時接続時は要設定）</summary>
        public HmxConnection connection { get; set; } = new HmxConnection();
        /// <summary>タッチ操作の感度倍率（回転/パン/ズーム）。起動時に InputManager へ反映</summary>
        public TouchSetting touch { get; set; } = new TouchSetting();
        /// <summary>write(JOG手動操作)用の事前共有トークン。空=writer無効。HMXの HMX_WRITE_TOKEN と同一値にする</summary>
        public string writeToken { get; set; } = "";
        /// <summary>JOGハートビート間隔(ms)。HMXと同一値（docs/hmx-link_write要求.md §8）</summary>
        public int jogIntervalMs { get; set; } = 100;
        /// <summary>JOGデッドマンTout(ms)。HMXの jogTimeoutMs と同一値</summary>
        public int jogTimeoutMs { get; set; } = 300;
    }

    /// <summary>手動操作(JOG)1ボタン。軸の向きごとに hmx-link へ write する専用デバイスを定義。</summary>
    [Serializable]
    public class ManualOp
    {
        public int axis { get; set; }             // 0=X/1=Y/2=Z（ActionInfo.axis と整合）
        public int dir { get; set; }              // +1/-1（軸のどちら向きか＝ハンドル位置）
        public string label { get; set; } = "";   // 表示名（前進/後退 等）
        public string dev { get; set; } = "";     // hmx-link へ write するデバイス（allow対象）
        public string lamp { get; set; } = "";    // PLCがボタン認識を返すランプ用 読取デバイス（内部IO）。空=ランプ無し（押下即点灯）
        public string interlock { get; set; } = ""; // HMX側操作許可の読取デバイス（内部IO）。OFF/不明=操作不可（ボタン灰色）。空=制約なし
        public string tag { get; set; } = "";     // 参考タグ（内部シム/実PLC直結時）
        public int onValue { get; set; } = 1;
        public string mode { get; set; } = "jog"; // jog=押下中ON・デッドマン
    }

    /// <summary>ユニットごとの手動操作定義（StreamingAssets/Datas/ManualOpInfo.json）。</summary>
    [Serializable]
    public class ManualOpData
    {
        public string mechId { get; set; } = "";
        public string name { get; set; } = "";
        public string group { get; set; } = "";          // 最上位の親（部名）。UnitInfo の parent チェイン最上位
        public List<string> path { get; set; } = new();  // 最上位→自分の祖先名（階層保持）
        public List<ManualOp> ops { get; set; } = new();
    }

    [Serializable]
    public class TimeChartDevice
    {
        public enum DeviceType {
            Internal,
            External,
            Sensor
        }
        public class Position
        {
            public string name { get; set; } = "";
            public string tagIn { get; set; } = "";
            public string tagOut { get; set; } = "";
            public string devIn { get; set; } = "";
            public string devOut { get; set; } = "";
            public int sizeIn { get; set; }
            public int sizeOut { get; set; }
            public float pos { get; set; }
            public float start { get; set; }
            public float time { get; set; }
            [JsonIgnore]
            public TagInfo tagInInfo { get; set; }
            [JsonIgnore]
            public TagInfo tagOutInfo { get; set; }
        };
        /// <summary>
        /// 名前
        /// </summary>
        public string name { get; set; } = "";
        /// <summary>
        /// グループ
        /// </summary>
        public string group { get; set; } = "";
        /// <summary>
        /// デバイスタイプ
        /// </summary>
        public DeviceType devType { get; set; } = DeviceType.Internal;
        /// <summary>
        /// 位置情報
        /// </summary>
        public List<Position> positions { get; set; } = new();
    }
    [Serializable]
    public class TimeChartData
    {
        public string mechId { get; set; } = "";
        public int cycle { get; set; }
        public List<TimeChartDevice> datas { get; set; } = new();
    }
}