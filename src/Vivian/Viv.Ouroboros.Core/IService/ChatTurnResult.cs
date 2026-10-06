using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 一轮对话的内部结果：服务内部流转用，对外由服务包成 VivApiResult
    /// </summary>
    /// <param name="Text">助手回复正文</param>
    /// <param name="InputTokens">本轮输入 token</param>
    /// <param name="OutputTokens">本轮输出 token</param>
    /// <param name="PendingApprovalId">本轮产生待审批时的审批单标识</param>
    /// <param name="Error">没跑起来时的原因</param>
    public sealed record ChatTurnResult(
        string? Text,
        int? InputTokens,
        int? OutputTokens,
        Guid? PendingApprovalId,
        string? Error = null);
}
