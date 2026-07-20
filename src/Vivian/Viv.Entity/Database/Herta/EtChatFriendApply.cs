using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 好友申请表
    /// </summary>
    public class EtChatFriendApply : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 申请人聊天账号ID
        /// </summary>
        public long ApplyChatUserId { get; set; }

        /// <summary>
        /// 被申请人聊天账号ID
        /// </summary>
        public long TargetChatUserId { get; set; }

        /// <summary>
        /// 申请验证留言
        /// </summary>
        [StringLength(200)]
        public string? Remark { get; set; }

        /// <summary>
        /// 申请状态：0待处理 1同意 2拒绝
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 处理时间
        /// </summary>
        public DateTime? HandleAt { get; set; }

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