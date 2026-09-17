using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Events;
using Viv.Contracts.Interface;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 单个事件类型的分发器 —— 一个事件类型对应一个实例。
    ///
    /// 之所以绕这一层、而不在 flush 时用 <see cref="IServiceProvider"/> 反射解析处理器：
    /// Autofac 作为根容器时注入的 <c>IServiceProvider</c> 很可能解析到根作用域，
    /// 会把 Scoped 的处理器（以及它依赖的 Scoped IMomoDbContext）解析到根上去 ——
    /// 那正好摧毁本地事件唯一的支点「与发布方同一作用域」。
    ///
    /// 改成启动期注册闭合泛型、运行期构造注入：<see cref="LocalEventHandlerInvoker{TEvent}"/>
    /// 构造注入 <c>IEnumerable&lt;IVivLocalEventHandler&lt;TEvent&gt;&gt;</c>，由 Autofac
    /// 把整条依赖链从当前作用域解析；总线按 <see cref="EventType"/> 建字典缓存。
    /// flush 时零反射、零容器查询。
    /// </summary>
    internal interface ILocalEventHandlerInvoker
    {
        /// <summary>本分发器负责的事件类型</summary>
        Type EventType { get; }

        /// <summary>按注册顺序依次执行该事件的全部处理器</summary>
        Task InvokeAsync(LocalEvent @event, CancellationToken ct);
    }
}
