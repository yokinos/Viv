using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天好友关系表
    /// </summary>
    public class EtChatUserRelation : EntityBase, ITenant, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 当前用户ChatId
        /// </summary>
        public long SelfChatUserId { get; set; }

        /// <summary>
        /// 好友ChatId
        /// </summary>
        public long TargetChatUserId { get; set; }

        /// <summary>
        /// 好友状态 0待确认 1已添加好友
        /// </summary>
        public int RelationStatus { get; set; }

        /// <summary>
        /// 添加好友时间
        /// </summary>
        public DateTime CreateAt { get; set; }

        /// <summary>
        /// 拉黑时间
        /// </summary>
        public DateTime? BlackAt { get; set; }

        /// <summary>
        /// 软删除标记
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
        /// <summary>
        /// 创建时间（UTC）—— 由 MomoDatabase 自动盖章，业务代码不写
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间（UTC）—— 每次更新自动盖章，业务代码不写
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人（IVivContext.UserId）—— 无登录上下文（后台任务）时为 null
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
