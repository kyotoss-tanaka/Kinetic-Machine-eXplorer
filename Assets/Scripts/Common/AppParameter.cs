using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using UnityEngine;

namespace Parameters
{
    public enum RobotType
    {
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
        /// 川重パラレル
        /// </summary>
        YF03N4,
        /// <summary>
        /// 川重6軸
        /// </summary>
        RS007L,
        /// <summary>
        /// 未定義
        /// </summary>
        UNDEFINED
    }

    [Serializable]
    public class PostgresSetting
    {
        public int No { get; set; }
        public int Type { get; set; }
        public int Cycle { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public int ClientMode { get; set; }
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
        public bool isPostgres
        {
            get
            {
                return Type == 0;
            }
        }
        public bool isMongo
        {
            get
            {
                return Type == 1;
            }
        }
        public bool isMqtt
        {
            get
            {
                return Type == 2;
            }
        }
        public bool isInner
        {
            get
            {
                return Type == 3;
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
        /// グループオブジェクト
        /// </summary>
        public string group { get; set; }
        /// <summary>
        /// 親オブジェクト
        /// </summary>
        public string parent { get; set; }
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
        public RobotSetting robotSetting;
        /// <summary>
        /// ワーク生成設定
        /// </summary>
        public WorkCreateSetting workSetting;
        /// <summary>
        /// ワーク生成設定
        /// </summary>
        public WorkDeleteSetting workDeleteSetting;
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
        /// <summary>
        /// スイッチ設定
        /// </summary>
        public SwitchSetting switchSetting;
        /// <summary>
        /// シグナルタワー設定
        /// </summary>
        public SignalTowerSetting towerSetting;
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
        /// 加速度設定 0:加速度(G) 1:時間
        /// </summary>
        public int acl { get; set; }
        /// <summary>
        /// 動作タグ
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 通信遅れ時間
        /// </summary>
        public int delay { get; set; }
        /// <summary>
        /// サイクル時間
        /// </summary>
        public int cycle { get; set; }
        /// <summary>
        /// 動作設定
        /// </summary>
        public List<UnitAction> actions { get; set; }

        public bool isInternal
        {
            get
            {
                return mode == 0 || mode == 1;
            }
        }
        public bool isExternal
        {
            get
            {
                return mode == 2 || mode == 3;
            }
        }
        public bool isRobo
        {
            get
            {
                return mode == 4;
            }
        }
        public bool isPlanarMotor
        {
            get
            {
                return mode == 5;
            }
        }
        public bool isConveyer
        {
            get
            {
                return mode == 6;
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
        public int target { get; set; }
        /// <summary>
        /// オフセット
        /// </summary>
        public int offset { get; set; }
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
        public int dir { get; set; }
        [JsonIgnore]
        public UnitSetting setting { get; set; }
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
        /// ヘッドユニット設定
        /// </summary>
        public UnitSetting headUnit { get; set; }
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

                    case "YF03N4":
                        return RobotType.YF03N4;

                    case "RS007L":
                        return RobotType.RS007L;

                    default:
                        return RobotType.UNDEFINED;

                }
            }
        }
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
        /// ヘッドユニット
        /// </summary>
        public string head { get; set; }
        /// <summary>
        /// ヘッドユニット設定
        /// </summary>
        public UnitSetting headUnit { get; set; }
    }


    [Serializable]
    public class ConveyerSetting
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
        /// 動作軸 0:X 1:Y 2:Z
        /// </summary>
        public int axis { get; set; }
        /// <summary>
        /// 方向
        /// </summary>
        public int dir { get; set; }
        /// <summary>
        /// 速度
        /// </summary>
        public float spd { get; set; }
        /// <summary>
        /// 加速力
        /// </summary>
        public float force { get; set; }
        /// <summary>
        /// 静止摩擦係数
        /// </summary>
        public float staticFriction { get; set; }
        /// <summary>
        /// 動摩擦係数
        /// </summary>
        public float dynamicFriction { get; set; }
        /// <summary>
        /// 動作タグ
        /// </summary>
        public string actTag { get; set; }
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
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
        /// <summary>
        /// オフセット(角度)
        /// </summary>
        public List<float> rot { get; set; }

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
        /// タグ名
        /// </summary>
        public string tag { get; set; }
        /// <summary>
        /// 距離
        /// </summary>
        public float distance { get; set; }
        /// <summary>
        /// オフセット(位置)
        /// </summary>
        public List<float> pos { get; set; }
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
        /// 形
        /// </summary>
        public List<UnitShape> datas { get; set; }
    }

    [Serializable]
    public class UnitShape
    {
        /// <summary>
        /// 中心点
        /// </summary>
        public List<float> center { get; set; }
        /// <summary>
        /// サイズ
        /// </summary>
        public List<float> size { get; set; }
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
    }

    [Serializable]
    public class BuildConfig
    {
        public string name { get; set; }
        public string mechId { get; set; }
        public bool isRelease { get; set; }
        public bool isVR { get; set; }
        public bool isMR { get; set; }
    }
}