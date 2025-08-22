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
            [SerializeField]
            public string Name { get; set; } = "";

            /// <summary>
            /// データタグ
            /// </summary>
            [SerializeField]
            public string DataTag { get; set; } = "";

            /// <summary>
            /// データ型
            /// </summary>
            [SerializeField]
            public DBSetting.eDeviceSize DataType { get; set; } = DBSetting.eDeviceSize.None;

            /// <summary>
            /// データ数
            /// </summary>
            [SerializeField]
            public int DataCount { get; set; } = 1;

            /// <summary>
            /// オフセット
            /// </summary>
            [SerializeField]
            public int Offset { get; set; } = 0;
        }

        /// <summary>
        /// 名称
        /// </summary>
        [SerializeField]
        public string Name { get; set; } = "";

        /// <summary>
        /// データタグ
        /// </summary>
        [SerializeField]
        public string DataTag { get; set; } = "";

        /// <summary>
        /// ユニットタグリスト
        /// </summary>
        [SerializeField]
        public List<UnitTag> UnitTags { get; set; } = new();
    }
}
