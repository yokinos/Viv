using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 群邀请记录表
    /// </summary>
    public class EtGroupChatInvite : EntityBase, ITenant, ISoftDelete
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
        public DateTime? DeletedAt { get; set; }
    }
}