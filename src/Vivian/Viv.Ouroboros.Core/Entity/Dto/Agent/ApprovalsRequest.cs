using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 审批控制器的请求集合
    /// </summary>
    /// <param name="ApprovalId">审批单标识（不是 TraceId）</param>
    /// <param name="Remark">审批意见：批准时的备注，或拒绝的原因</param>
    public sealed record ApprovalDecisionRequest(Guid ApprovalId, string? Remark);
}
