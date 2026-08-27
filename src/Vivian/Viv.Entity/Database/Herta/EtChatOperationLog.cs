using System;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 聊天操作审计日志
    /// </summary>
    public class EtChatOperationLog : EntityBase, ITenant, ISoftDeleted
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 操作人ChatUserId
        /// </summary>
        public long OperateChatUserId { get; set; }

        /// <summary>
        /// 操作类型：撤回/踢人/拉黑/修改群信息/同意好友等
        /// </summary>
        public int OperateType { get; set; }

        /// <summary>
        /// 关联对象ID：消息ID/群ID/对方用户ID
        /// </summary>
        public long TargetId { get; set; }

        /// <summary>
        /// 操作详情
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 操作时间
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