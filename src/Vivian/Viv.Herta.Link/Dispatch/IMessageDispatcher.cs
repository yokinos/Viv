using Viv.EventContracts.Herta;
using Viv.Nana;

namespace Viv.Herta.Link.Dispatch
{
    public interface IMessageDispatcher
    {
        /// <returns>
        /// 成功 = 已尝试投递（目标不在本机也算成功）。
        /// 失败一律 <c>IsRequeue = false</c>：重投还是同样的推送失败，缺口由客户端补拉。
        /// </returns>
        Task<SubscribeResult> DispatchAsync(
            NanaEnvelope<SendMessageEvent> envelope,
            SendMessageEvent evt,
            CancellationToken cancellationToken = default);

        Task<SubscribeResult> DispatchRecallAsync(
            NanaEnvelope<MessageRecalledEvent> envelope,
            MessageRecalledEvent evt,
            CancellationToken cancellationToken = default);
    }
}
