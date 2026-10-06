using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 全局配置项（值统一按字符串存，读取方按 ValueType 转换）。
    /// 例：Conversation.MaxRetainedTurns —— 每个会话保留多少轮；
    /// 再例：Conversation.CompactionTriggerMessages —— 超过多少条触发压缩。
    /// </summary>
    public class OtConfig : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 配置键，建议用 分组.项 的形式，如 Conversation.MaxRetainedTurns
        /// </summary>
        public string ConfigKey { get; set; } = string.Empty;

        /// <summary>
        /// 配置值（字符串形式存储）
        /// </summary>
        public string? ConfigValue { get; set; }

        /// <summary>
        /// 值类型提示：1=字符串 2=整数 3=小数 4=布尔 5=JSON
        /// </summary>
        public int ValueType { get; set; } = 1;

        /// <summary>
        /// 备注：这项配置的作用与建议取值
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人Id
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人Id
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
