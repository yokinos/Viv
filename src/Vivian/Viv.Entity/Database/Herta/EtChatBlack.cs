using System;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天黑名单表
    /// </summary>
    public class EtChatBlack : EntityBase, ITenant, ISoftDelete
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 操作人
        /// </summary>
        public long SelfChatUserId { get; set; }

        /// <summary>
        /// 被拉黑用户ChatId
        /// </summary>
        public long TargetChatUserId { get; set; }

        /// <summary>
        /// 拉黑时间
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