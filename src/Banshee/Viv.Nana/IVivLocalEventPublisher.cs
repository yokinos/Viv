using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// 本地事件发布器 —— 进程内异步队列，走 Wolverine 本地队列，不出网。
    /// 与 <see cref="IVivEventPublisher"/> 平行、互不继承、互不引用，泛型约束是唯一的类型防线。
    /// </summary>
    public interface IVivLocalEventPublisher
    {
        /// <summary>
        /// 发布本地事件。入队即返回，不等待消费。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>content 为 null 时 false；入队成功 true。</returns>
        ValueTask<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent;

        /// <summary>
        /// 发布延迟本地事件，delayTTL 到点后才进队列。
        /// 延迟是纯内存调度，进程重启后未到期的消息丢失（与 <see cref="IVivEventPublisher.PublishDelayAsync{T}(TimeSpan, T, CancellationToken)"/> 行为一致）。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="delayTTL">延迟时长，负数视为入参无效</param>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>入参无效时 false；调度成功 true。</returns>
        ValueTask<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaLocalEvent;
    }
}
