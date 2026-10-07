using System;
using Viv.Nana;

namespace Viv.EventContracts.Ouroboros
{
    /// <summary>
    /// 跑一轮对话的跨进程事件：Api 落好用户消息后投出，Worker 消费并跑完这一轮。
    /// 主体/用户身份刻意不放这里 —— 它随信封的 Context 传播，由 VivConsumer 基类水合进 IVivContext；
    /// 事件里再放一份就成了第二个真相来源。
    /// </summary>
    public class OuroborosTurnEvent : NanaEvent
    {
        public OuroborosTurnEvent() { }

        /// <summary>
        /// 会话对外标识（OtConversation.ConversationKey）
        /// </summary>
        public Guid ConversationKey { get; set; }

        /// <summary>
        /// 触发本轮的已落库用户消息（OtMessage.Id）—— 消费端靠它定位"要跑哪一轮"
        /// </summary>
        public long UserMessageId { get; set; }

        /// <summary>
        /// 用户输入正文（与那条用户消息的 Content 一致，省消费端一次读库）
        /// </summary>
        public string Text { get; set; } = string.Empty;
    }
}
