using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Parameters.DBSetting;
using UnityEngine;

namespace Parameters
{
    [Serializable]
    public class KMXDBSetting : DBSetting
    {
        /// <summary>
        /// 接続先名
        /// </summary>
        public string connectionName { get; set; } = "";

        /// <summary>
        /// プロトコル名
        /// </summary>
        public eProtocolType protocol { get; set; } = eProtocolType.None;

        /// <summary>
        /// IPアドレス
        /// </summary>
        public string ipAddress { get; set; } = "";

        /// <summary>
        /// ポート番号
        /// </summary>
        public int port { get; set; }

        /// <summary>
        /// ネットワークアドレス
        /// </summary>
        public int NetAddress { get; set; }

        /// <summary>
        /// PC番号
        /// </summary>
        public int PcNo { get; set; }


        /// <summary>
        /// ユニットタグ設定
        /// </summary>
        [SerializeField]
        public UnitTagSetting unitTag { get; set; }

        /// <summary>
        /// 全データ数
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public int AllDataCount
        {
            get
            {
                return DataCount * ((unitTag != null) ? unitTag.UnitTags.Count : 1);
            }
        }
        /// <summary>
        /// DBデータ
        /// </summary>
        public List<TagInfo?> values = new();

        /// <summary>
        /// ソート用データ
        /// </summary>
        public List<KMXDBSetting> sortedDatas = new();
    }
}

