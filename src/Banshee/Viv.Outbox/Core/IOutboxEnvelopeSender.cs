using Viv.Nana;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 一条已持久化消息的「重建 + 重发」。
    ///
    /// <para>
    /// 抽成接口是为了把<b>运行期类型派发</b>收敛到一处：投递器手上只有一条字符串
    /// <c>EventType</c> 和一段 payload，得先在闭合泛型上重建出 <c>NanaEnvelope&lt;T&gt;</c>
    /// 才能调发布器。<see cref="OutboxEnvelopeSender{T}"/> 就是那个闭合泛型，
    /// 每个事件类型只 <c>MakeGenericType</c> 一次。
    /// </para>
    ///
    /// <para>
    /// 发布器是<b>逐次传进来</b>而不是构造注入的：这样发送器本身不持有作用域内的任何东西，
    /// 于是可以整个进程缓存一份，不必每轮轮询随 scope 重建。
    /// </para>
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

            // MessageId 是消费端 nana:{ServiceName}:{EventType}:{MessageId} 消费锁的去重键。
            // 以**列**为准钉回去：payload 里那个可能来自更早的写法，而属性初始化器
            // 会在反序列化缺字段时悄悄新生成一个 —— 那等于每次重投都换一把去重键。
            envelope.MessageId = messageId;

            return await publisher.PublishEnvelopeAsync(envelope, cancellationToken);
        }
    }
}
