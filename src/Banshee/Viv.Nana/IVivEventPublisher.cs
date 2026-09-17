using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Nana
{
    public interface IVivEventPublisher
    {
        /// <summary>
        /// 发布普通消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>content 为 null 时 false；发布成功 true。传输失败抛连接异常。</returns>
        ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent;

        /// <summary>
        /// 发布信封（<b>原样重发</b>）—— 保留 MessageId / Context / ReDeliverCount / CreatedAt，
        /// 且<b>不重新盖 holderId</b>（投递的是一个已经冻结的信封，与信封版延迟重投行为一致）。
        /// 供发件箱（Viv.Outbox）把持久化过的信封重新投递上线。
        /// ⚠️ 必须走这个方法而不是内容版：内容版每次新建信封，MessageId 会被重新生成 ——
        /// 而 MessageId 正是消费端 <c>nana:{ServiceName}:{EventType}:{MessageId}</c> 消费锁的去重键。
        /// <para>
        /// 刻意<b>不叫</b> <c>PublishAsync</c> 重载：那样与内容版同为一个参数，
        /// 调用点写 <c>PublishAsync&lt;T&gt;(null)</c> 或 <c>PublishAsync(null!)</c> 时
        /// <c>null</c> 字面量对两个重载都成立且互不更优 → CS0121 二义。换个名字彻底躲开。
        /// </para>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="envelope"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>envelope 或其 Content 为 null 时 false；发布成功 true。传输失败抛连接异常。</returns>
        ValueTask<bool> PublishEnvelopeAsync<T>(NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent;

        /// <summary>
        /// 发布延迟消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="delayTTL"></param>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>入参无效时 false；调度成功 true。传输失败抛连接异常。</returns>
        ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent;

        /// <summary>
        /// 发布延迟消息（信封重投）—— 直接调度原信封，保留 MessageId/ReDeliverCount/DelaySecond/Context，
        /// 供 VivConsumer 延迟重投计数跟踪（内容版重载会新建信封，丢失这些元数据）。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="delayTTL"></param>
        /// <param name="envelope"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>入参无效时 false；调度成功 true。传输失败抛连接异常。</returns>
        ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent;
    }
}
