using System;

namespace Viv.Ouroboros.Core.Entity.Dto.Agent
{
    /// <summary>
    /// 投递一轮对话到队列的请求（自检端点用）
    /// </summary>
    /// <param name="ConversationKey">会话标识</param>
    /// <param name="Text">用户输入正文；重投模式（带 UserMessageId）下可省略，正文以库里那条消息为准</param>
    /// <param name="UserMessageId">
    /// 可选。传了就是**重投模式**：认领这条已落库的用户消息、不再新建，投出的事件与原投递完全同一个 UserMessageId，
    /// 用来验证消费端幂等（重投递不能把这一轮跑两遍）。不传就是正常投递：先落一条用户消息再投。
    /// </param>
    public sealed record QueueTurnRequest(Guid ConversationKey, string? Text, long? UserMessageId = null);
}
