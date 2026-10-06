using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 会话控制器的请求集合
    /// </summary>
    public sealed record CreateConversationRequest(string MainAgentKey, string? Title);

    /// <summary>
    /// 发消息请求
    /// </summary>
    public sealed record SendMessageRequest(string Text);
}
