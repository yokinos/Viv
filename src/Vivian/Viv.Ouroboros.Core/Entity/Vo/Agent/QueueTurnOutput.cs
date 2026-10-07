using System;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 投递一轮对话的结果：用户消息已落库，事件已投出，等 Worker 消费
    /// </summary>
    public sealed record QueueTurnOutput
    {
        /// <summary>
        /// 用户消息是否已落库并成功投递
        /// </summary>
        public bool Ok { get; set; }

        /// <summary>
        /// 已落库的用户消息 Id（OtMessage.Id），与 DB 里那条 user 消息一致
        /// </summary>
        public long UserMessageId { get; set; }
    }
}
