using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Momo.Attributes
{
    /// <summary>
    /// 归档表标记(标记当前实体的归档表是哪些)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class HistoryTableAttribute : Attribute
    {
        /// <summary>
        /// 历史表是哪些
        /// </summary>
        public string[] TableNames { get; set; } = [];

        /// <summary>
        /// 多久后归档（会根据 CreatedAt 字段自动归档）
        /// </summary>
        public int KeepDays { get; set; } = 365;

        public HistoryTableAttribute(params string[] tables)
        {
            TableNames = tables;
        }

        public HistoryTableAttribute(int days, string[] tables)
        {
            KeepDays = days;
            TableNames = tables;
        }
    }
}
