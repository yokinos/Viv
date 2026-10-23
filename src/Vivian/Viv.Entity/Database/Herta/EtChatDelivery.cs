using System;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 投递水位：每个用户一行，记录客户端已经确认收到的最大消息 Id。
    /// 客户端重连时用 Resync 拿它和本地比对，决定要不要补拉。
    /// 一行一个用户是为了让 Ack 只更新一行，不必按消息逐条记。
    /// </summary>
    public class EtChatDelivery : EntityBase, ITenant, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 用户ID（接收方）
        /// </summary>
        public long UserId { get; set; }

        /// <summary>
        /// 已确认收到的最大消息ID，只进不退
        /// </summary>
        public long LastDeliveredMessageId { get; set; }

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
