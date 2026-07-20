using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 敏感词命中审计日志
    /// </summary>
    public class EtChatSensitiveLog : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 发送人ChatUserId
        /// </summary>
        public long SenderChatUserId { get; set; }

        /// <summary>
        /// 会话类型 0私聊 1群聊
        /// </summary>
        public int ChatType { get; set; }

        /// <summary>
        /// 私聊对方Id / 群Id
        /// </summary>
        public long TargetId { get; set; }

        /// <summary>
        /// 原始消息内容
        /// </summary>
        public string? OriginContent { get; set; }

        /// <summary>
        /// 命中敏感词集合逗号分隔
        /// </summary>
        [StringLength(1000)]
        public string? HitWords { get; set; }

        /// <summary>
        /// 处理结果 0替换放行 1拦截失败 2告警放行
        /// </summary>
        public int HandleResult { get; set; }

        /// <summary>
        /// 记录时间
        /// </summary>
        public DateTime CreateAt { get; set; }

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