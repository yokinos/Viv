using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;
using Viv.Contracts.Interface;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 闭合泛型分发器 —— 由 LocalEventRegistration 在启动期按扫到的事件类型 MakeGenericType 注册进容器。
    /// 泛型参数在这里固化成 <see cref="EventType"/>，flush 时只需一次字典查表，不必按运行时类型反射构造。
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    internal sealed class LocalEventHandlerInvoker<TEvent> : ILocalEventHandlerInvoker
        where TEvent : LocalEvent
    {
        private readonly IEnumerable<IVivLocalEventHandler<TEvent>> _handlers;

        public LocalEventHandlerInvoker(IEnumerable<IVivLocalEventHandler<TEvent>> handlers)
        {
            _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        }

        /// <inheritdoc />
        public Type EventType => typeof(TEvent);

        /// <inheritdoc />
        public async Task InvokeAsync(LocalEvent @event, CancellationToken ct)
        {
            var typed = (TEvent)@event;

            // 顺序 await：同一事件的多个处理器按注册顺序执行，前一个抛异常即中断后续
            foreach (var handler in _handlers)
            {
                await handler.HandleAsync(typed, ct).ConfigureAwait(false);
            }
        }
    }
}
