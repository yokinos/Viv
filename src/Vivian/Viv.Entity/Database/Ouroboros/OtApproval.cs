using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 人工审批单（HITL）：写操作与不可逆操作执行前必须在这里留一张单子，
    /// 人工批准后才放行；拒绝或超时则把结果作为工具结果回给模型。
    /// </summary>
    public class OtApproval : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 审批单本身的主键标识（GUID），对外接口用它定位"批准哪一张"。
        /// 注意与 <see cref="ExternalRequestId"/> 不是一回事：这个是我们自己的，那个是 MAF 的。
        /// </summary>
        public Guid ApprovalId { get; set; }

        /// <summary>
        /// 所属会话（OtConversation.Id）
        /// </summary>
        public long ConversationId { get; set; }

        /// <summary>
        /// 关联工具调用（OtToolCall.Id）
        /// </summary>
        public long? ToolCallId { get; set; }

        /// <summary>
        /// MAF 的审批请求 id（ToolApprovalRequestContent.RequestId）—— **框架侧**的标识，
        /// 续跑时用来构造 ToolApprovalResponseContent(RequestId, approved, toolCall)。
        /// 不要拿它替代 <see cref="ApprovalId"/>：格式与生命周期都由框架决定。
        /// </summary>
        public string? ExternalRequestId { get; set; }

        /// <summary>
        /// 被审批的那次工具调用的 CallId（ToolCallContent.CallId），续跑时用来重建 toolCall 内容
        /// </summary>
        public string? ExternalToolCallId { get; set; }

        /// <summary>
        /// 隔离主体（subjectId）
        /// </summary>
        public long? SubjectId { get; set; }

        /// <summary>
        /// 待审批的工具键
        /// </summary>
        public string ToolKey { get; set; } = string.Empty;

        /// <summary>
        /// 待审批的入参快照（JSON），批准的就是它
        /// </summary>
        public string? Arguments { get; set; }

        /// <summary>
        /// 审批状态：1=待审 2=已批准 3=已拒绝 4=已超时
        /// </summary>
        public int Status { get; set; } = 1;

        /// <summary>
        /// 发起审批的时间
        /// </summary>
        public DateTime? RequestedAt { get; set; }

        /// <summary>
        /// 审批过期时间，超时按拒绝处理
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// 审批人Id
        /// </summary>
        public long? DecidedBy { get; set; }

        /// <summary>
        /// 审批时间
        /// </summary>
        public DateTime? DecidedAt { get; set; }

        /// <summary>
        /// 审批意见（拒绝原因等）
        /// </summary>
        public string? DecisionRemark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人Id
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人Id
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
