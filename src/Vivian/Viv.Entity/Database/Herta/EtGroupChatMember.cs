using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 群聊成员表
    /// </summary>
    public class EtGroupChatMember : EntityBase, ITenant, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 群ID，关联 EtGroupChat.Id
        /// </summary>
        public long GroupChatId { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 群内昵称
        /// </summary>
        [StringLength(100)]
        public string? GroupNickName { get; set; }

        /// <summary>
        /// 成员身份 0普通成员 1管理员 2群主
        /// </summary>
        public int MemberRole { get; set; }

        /// <summary>
        /// 是否单独禁言该成员
        /// </summary>
        public bool IsMute { get; set; }

        /// <summary>
        /// 单人禁言到期时间，null永久禁言
        /// </summary>
        public DateTime? MuteEndAt { get; set; }

        /// <summary>
        /// 消息免打扰
        /// </summary>
        public bool NoDisturb { get; set; }

        /// <summary>
        /// 是否置顶该群会话
        /// </summary>
        public bool IsTopSession { get; set; }

        /// <summary>
        /// 入群时间
        /// </summary>
        public DateTime JoinAt { get; set; }

        /// <summary>
        /// 退群时间，未退群为null
        /// </summary>
        public DateTime? QuitAt { get; set; }

        /// <summary>
        /// 最后已读消息ID
        /// </summary>
        public long LastReadMsgId { get; set; }

        /// <summary>
        /// 软删除标记（退群逻辑也可逻辑删除本条成员记录）
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
