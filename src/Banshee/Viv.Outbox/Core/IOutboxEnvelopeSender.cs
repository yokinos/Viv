using Viv.Nana;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 一条已持久化消息的「重建 + 重发」。
    ///
    /// 抽成接口是为了把运行期类型派发收敛到一处：投递器手上只有 EventType 字符串和一段 payload，
    /// 得先在闭合泛型上重建出 <c>NanaEnvelope&lt;T&gt;</c> 才能调发布器。
    /// 发布器逐次传入而不构造注入，发送器因此无状态，可以整个进程缓存一份。
    /// </summary>
    internal interface IOutboxEnvelopeSender
    {
        /// <summary>
        /// 反序列化 payload 成信封、把 <paramref name="messageId"/> 钉回列里的值，然后原样重发。
        /// </summary>
        /// <returns>payload 为空 / 反序列化出 null / Content 为 null 时 false。传输失败抛连接异常。</returns>
        ValueTask<bool> SendAsync(
            IVivEventPublisher publisher,
            string payload,
            long messageId,
            CancellationToken cancellationToken);
    }

    /// <summary>闭合泛型的发送器：重建 + 重发全程零反射。</summary>
    internal sealed class OutboxEnvelopeSender<T> : IOutboxEnvelopeSender where T : NanaEvent
    {
        public async ValueTask<bool> SendAsync(
            IVivEventPublisher publisher,
            string payload,
            long messageId,
            CancellationToken cancellationToken)
        {
            var envelope = System.Text.Json.JsonSerializer.Deserialize<NanaEnvelope<T>>(payload, OutboxJson.Options);
            if (envelope?.Content is null) return false;

            // 以列里的值为准：payload 里那个可能来自更早的写法，而属性初始化器会在
            // 反序列化缺字段时悄悄新生成一个 —— 那等于每次重投都换一把去重键。
            envelope.MessageId = messageId;

            return await publisher.PublishEnvelopeAsync(envelope, cancellationToken);
        }
    }
}
