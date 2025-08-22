using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Parameters
{
    [Serializable]
    public class DBSetting
    {
        /// <summary>
        /// プロトコルのタイプ
        /// </summary>
        public enum eProtocolType
        {
            /// <summary>
            /// MCプロトコル
            /// </summary>
            McProtocol = 0,
            /// <summary>
            /// MCプロトコル(UDP)
            /// </summary>
            McProtocol_UDP,
            /// <summary>
            /// MICKS
            /// </summary>
            MICKS,
            /// <summary>
            /// FTP
            /// </summary>
            FTP,
            /// <summary>
            /// HTTP
            /// </summary>
            HTTP,
            /// <summary>
            /// VAPIX
            /// </summary>
            VAPIX,
            /// <summary>
            /// FINS/TCP
            /// </summary>
            FINS_TCP,
            /// <summary>
            /// FINS/TCP
            /// </summary>
            FINS_UDP,
            /// <summary>
            /// MCプロトコル(キーエンス)
            /// </summary>
            McProtocol_KEYENCE,
            /// <summary>
            /// LOADERTCP
            /// </summary>
            LOADERTCP,
            /// <summary>
            /// OPC UA通信プロトコル
            /// </summary>
            OPC_UA,
            /// <summary>
            /// 接続先なし
            /// </summary>
            None
        }

        /// <summary>
        /// デバイスサイズ
        /// </summary>
        public enum eDeviceSize
        {
            /// <summary>
            /// 定義なし
            /// </summary>
            None,
            /// <summary>
            /// ワード
            /// </summary>
            W,
            /// <summary>
            /// ダブルワード
            /// </summary>
            DW,
            /// <summary>
            /// クワッドワード
            /// </summary>
            QW,
            /// <summary>
            /// ビット
            /// </summary>
            Bit,
            /// <summary>
            /// 文字列
            /// </summary>
            String,
            /// <summary>
            /// ユニットタグ
            /// </summary>
            UnitTag,
            /// <summary>
            /// 計算タグ
            /// </summary>
            CalcTag,
            /// <summary>
            /// システムタグ
            /// </summary>
            SystemTag,
            /// <summary>
            /// Bool型 0=false,1=true
            /// </summary>
            Bool,
            /// <summary>
            /// ロングワード
            /// </summary>
            LW,
            /// <summary>
            /// バイト
            /// </summary>
            Byte
        }

        /// <summary>
        /// 名称
        /// </summary>
        [SerializeField]
        public string Name { get; set; } = "";

        /// <summary>
        /// レジスタタイプ
        /// </summary>
        //public string RegisterType { get; set; } = "D";
        [SerializeField]
        public string RegisterType { get; set; } = "";

        /// <summary>
        /// レジスタ番号
        /// </summary>
        [SerializeField]
        public int RegisterNo { get; set; }

        /// <summary>
        /// ビット番号
        /// </summary>
        [SerializeField]
        public int BitNo { get; set; } = -1;

        /// <summary>
        /// プログラム番号
        /// </summary>
        [SerializeField]
        public int ProgramNo { get; set; }

        /// <summary>
        /// データ数
        /// </summary>
        [SerializeField]
        public int DataCount { get; set; } = 1;

        /// <summary>
        /// データ型
        /// </summary>
        [SerializeField]
        public eDeviceSize DataType { get; set; } = eDeviceSize.None;

        /// <summary>
        /// 受信データ数
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public int RcvDataCount { get; set; } = 0;
        /// <summary>
        /// 受信データ型
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public eDeviceSize RcvDataType { get; set; } = eDeviceSize.None;

        /// <summary>
        /// イベントID  いずれ削除
        /// </summary>
        public string EventId { get; set; } = "";

        /// <summary>
        /// データタグ
        /// </summary>
        [SerializeField]
        public string DataTag { get; set; } = "";

        /// <summary>
        /// ユニットタグ
        /// </summary>
        public string UnitTag { get; set; } = "";

        /// <summary>
        /// コネクションID
        /// </summary>
        public int ConnectionId { get; set; }

        ///// <summary>
        ///// 画面表示名   いずれ削除
        ///// </summary>
        //public string DispName { get; set; } = "";

        /// <summary>
        /// 書き込み用データフラグ
        /// </summary>
        [SerializeField]
        public bool IsWrite { get; set; } = false;

        /// <summary>
        /// モニター用データフラグ
        /// </summary>
        public bool IsMonitor { get; set; } = false;

        /// <summary>
        /// 符号なし
        /// </summary>
        public bool IsUnsigned { get; set; }

        /// <summary>
        /// リトルエンディアン(隠しデータ)
        /// </summary>
        public bool IsLittleEndian { get; set; } = false;

        /// <summary>
        /// ビットデータか
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public bool IsBitData
        {
            get
            {
                return BitNo >= 0;
            }
        }

        /// <summary>
        /// OPC UA NodeId
        /// </summary>
        public string NodeId { get; set; }

        /// <summary>
        /// 備考
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// クローン
        /// </summary>
        /// <returns></returns>
        public virtual object Clone()
        {
            var json = JsonSerializer.Serialize(this, GetType());
            return JsonSerializer.Deserialize(json, GetType())!;
        }
    }
}