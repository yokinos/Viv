using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 群聊主表
    /// </summary>
    public class EtGroupChat : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 聊天域 Id —— IM 自己的隔离键，与 Apex 主体无关，由本模块分配。
        /// 本实体刻意不实现 ITenant：Momo 的租户盖章与全局过滤器不再作用于它，
        /// 隔离一律靠查询里的显式条件，漏写一处就是跨域可见，改查询时务必带上。
        /// </summary>
        public long RealmId { get; set; }

        /// <summary>
        /// 群名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 群主用户ID
        /// </summary>
        public long OwnerUserId { get; set; }

        /// <summary>
        /// 群头像地址
        /// </summary>
        [StringLength(500)]
        public string? AvatarUrl { get; set; }

        /// <summary>
        /// 群简介/描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 群类型（自定义枚举区分业务群/班级群/活动群等）
        /// </summary>
        public int GroupType { get; set; }

        /// <summary>
        /// 群最大容纳人数
        /// </summary>
        public int MaxMemberCount { get; set; }

        /// <summary>
        /// 入群验证方式 0无需验证 1需要管理员同意
        /// </summary>
        public int JoinVerifyType { get; set; }

        /// <summary>
        /// 是否允许群成员发起群聊邀请
        /// </summary>
        public bool AllowMemberInvite { get; set; }

        /// <summary>
        /// 是否全员禁言
        /// </summary>
        public bool AllMute { get; set; }

        /// <summary>
        /// 禁言截止时间，null=永久禁言
        /// </summary>
        public DateTimeOffset? MuteEndAt { get; set; }

        /// <summary>
        /// 群创建时间
        /// </summary>
        public DateTimeOffset CreateAt { get; set; }

        /// <summary>
        /// 最后消息时间
        /// </summary>
        public DateTimeOffset? LastMessageAt { get; set; }

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
