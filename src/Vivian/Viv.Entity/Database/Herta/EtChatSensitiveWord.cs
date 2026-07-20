using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天敏感词过滤表
    /// </summary>
    public class EtChatSensitiveWord : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 敏感词文本
        /// </summary>
        [StringLength(200)]
        public string? Word { get; set; }

        /// <summary>
        /// 替换掩码，默认*
        /// </summary>
        [StringLength(10)]
        public string? MaskText { get; set; }

        /// <summary>
        /// 处理类型 0掩码替换 1拦截拒绝发送 2仅告警记录
        /// </summary>
        public int HandleType { get; set; }

        /// <summary>
        /// 敏感词分类 0通用违规 1广告 2涉政 3辱骂
        /// </summary>
        public int WordType { get; set; }

        /// <summary>
        /// 创建人ChatUserId
        /// </summary>
        public long CreateChatUserId { get; set; }

        /// <summary>
        /// 软删除标记
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}