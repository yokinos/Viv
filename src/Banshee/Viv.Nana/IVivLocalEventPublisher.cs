using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// 本地事件发布器 —— 进程内异步队列，走 Wolverine 本地队列（不出网）。
    ///
    /// 【与 <see cref="IVivEventPublisher"/> 的关系】两者**平行、互不继承、互不引用**：
    /// - <see cref="IVivEventPublisher"/>：<see cref="NanaEvent"/> → RabbitMQ → 另一个进程
    /// - 本接口：<see cref="NanaLocalEvent"/> → 进程内本地队列 → 后台线程 + 独立 DI 作用域
    ///
    /// 泛型约束是唯一的类型防线：拿 NanaEvent 调本接口、或拿 NanaLocalEvent 调
    /// <see cref="IVivEventPublisher"/>，都在编译期失败。
    ///
    /// 【发布即返回】消息入队后立刻返回，不等待消费 —— 这是本地队列相对本地总线
    /// （<c>IVivLocalEventBus</c>，handler 必须跑完才返回）的核心差别。
    /// </summary>
    public interface IVivLocalEventPublisher
    {
        /// <summary>
        /// 发布本地事件 —— 入队即返回，不等待消费。
        /// </summary>
        /// <typeparam name="T">本地事件类型，须继承 <see cref="NanaLocalEvent"/></typeparam>
        /// <param name="content">消息内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>content 为 null 时 false；入队成功 true。</returns>
        ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent;

        /// <summary>
        /// 发布延迟本地事件 —— delayTTL 到点后才进队列。
        ///
        /// ⚠️ <b>延迟是纯内存调度</b>：未配消息存储时 Wolverine 走 InMemoryScheduledJobProcessor，
        /// 进程重启后**未到期的消息丢失**。这与 <see cref="IVivEventPublisher.PublishDelayAsync{T}(TimeSpan, T, CancellationToken)"/>
        /// 当前的行为完全一致（同一条 NullMessageStore 路径），不是本接口引入的退步。
        /// 需要跨重启就必须引消息存储（PersistMessagesWithSqlServer 等）+ 建表，那是另一件事。
        /// </summary>
        /// <typeparam name="T">本地事件类型，须继承 <see cref="NanaLocalEvent"/></typeparam>
        /// <param name="delayTTL">延迟时长，负数视为入参无效</param>
        /// <param name="content">消息内容</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>入参无效时 false；调度成功 true。</returns>
        ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent;
    }
}
