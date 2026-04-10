using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace Parameters
{
    [Serializable]
    public class UnitTagSetting
    {
        [Serializable]
        public class UnitTag
        {
            /// <summary>
            /// 名称
            /// </summary>
            public string Name { get; set; } = "";

            /// <summary>
            /// データタグ
            /// </summary>
            public string DataTag { get; set; } = "";

            /// <summary>
            /// データ型
            /// </summary>
            public DBSetting.eDeviceSize DataType { get; set; } = DBSetting.eDeviceSize.None;

            /// <summary>
            /// データ数
            /// </summary>
            public int DataCount { get; set; } = 1;

            /// <summary>
            /// オフセット
            /// </summary>
            public int Offset { get; set; } = 0;
        }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// データタグ
        /// </summary>
        public string DataTag { get; set; } = "";

        /// <summary>
        /// ユニットタグリスト
        /// </summary>
        public List<UnitTag> UnitTags { get; set; } = new();
    }
}
