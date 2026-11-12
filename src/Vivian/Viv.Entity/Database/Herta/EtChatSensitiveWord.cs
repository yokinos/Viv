using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天敏感词过滤表
    /// </summary>
    public class EtChatSensitiveWord : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 聊天域 Id —— IM 自己的隔离键，与 Apex 主体无关，由本模块分配。
        /// 本实体刻意不实现 ITenant：Momo 的租户盖章与全局过滤器不再作用于它，
        /// 隔离一律靠查询里的显式条件，漏写一处就是跨域可见，改查询时务必带上。
        /// </summary>
        public long RealmId { get; set; }

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
        public DateTimeOffset? DeletedAt { get; set; }
        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章，业务代码不写
        /// </summary>
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章，业务代码不写
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
