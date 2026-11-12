using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 群邀请记录表
    /// </summary>
    public class EtGroupChatInvite : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 聊天域 Id —— IM 自己的隔离键，与 Apex 主体无关，由本模块分配。
        /// 本实体刻意不实现 ITenant：Momo 的租户盖章与全局过滤器不再作用于它，
        /// 隔离一律靠查询里的显式条件，漏写一处就是跨域可见，改查询时务必带上。
        /// </summary>
        public long RealmId { get; set; }

        /// <summary>
        /// 群ID，关联 EtGroupChat.Id
        /// </summary>
        public long GroupChatId { get; set; }

        /// <summary>
        /// 邀请人聊天账号ID
        /// </summary>
        public long InviterChatUserId { get; set; }

        /// <summary>
        /// 被邀请人聊天账号ID
        /// </summary>
        public long TargetChatUserId { get; set; }

        /// <summary>
        /// 邀请附言
        /// </summary>
        [StringLength(200)]
        public string? InviteRemark { get; set; }

        /// <summary>
        /// 邀请状态：0待处理 1同意入群 2拒绝邀请
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 邀请过期时间
        /// </summary>
        public DateTime? ExpireAt { get; set; }

        /// <summary>
        /// 用户处理邀请时间
        /// </summary>
        public DateTime? HandleAt { get; set; }

        /// <summary>
        /// 邀请创建时间
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
