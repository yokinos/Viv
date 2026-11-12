using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 用户主动申请加入群申请表
    /// </summary>
    public class EtGroupJoinApply : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 聊天域 Id —— IM 自己的隔离键，与 Apex 主体无关，由本模块分配。
        /// 本实体刻意不实现 ITenant：Momo 的租户盖章与全局过滤器不再作用于它，
        /// 隔离一律靠查询里的显式条件，漏写一处就是跨域可见，改查询时务必带上。
        /// </summary>
        public long RealmId { get; set; }

        /// <summary>
        /// 目标群ID，关联 EtGroupChat.Id
        /// </summary>
        public long GroupChatId { get; set; }

        /// <summary>
        /// 申请入群的用户ChatUserId
        /// </summary>
        public long ApplyChatUserId { get; set; }

        /// <summary>
        /// 申请验证留言
        /// </summary>
        [StringLength(200)]
        public string? ApplyRemark { get; set; }

        /// <summary>
        /// 处理人（群主/管理员ChatUserId）
        /// </summary>
        public long? HandleChatUserId { get; set; }

        /// <summary>
        /// 申请状态：0待审核 1同意入群 2拒绝申请
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 处理时间
        /// </summary>
        public DateTime? HandleAt { get; set; }

        /// <summary>
        /// 申请创建时间
        /// </summary>
        public DateTime CreateAt { get; set; }

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
