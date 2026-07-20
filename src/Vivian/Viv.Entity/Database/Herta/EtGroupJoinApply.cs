using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 用户主动申请加入群申请表
    /// </summary>
    public class EtGroupJoinApply : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

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
        public DateTime? DeletedAt { get; set; }
    }
}