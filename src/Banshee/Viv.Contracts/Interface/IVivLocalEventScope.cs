using System;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 本地事件作用域 —— 给没有 HTTP 过滤器 / 消费者 HandleAsync 的宿主用
    /// （TickerQ、手写 BackgroundService）。
    ///
    /// <see cref="RunAsync(Func{Task}, CancellationToken)"/> 成功则 Flush，失败则 Discard 后原样上抛。
    /// 分发发生在调用方清理 <c>IVivContext</c> 之前：本接口不清理上下文，调用方在本方法返回后再 Clear。
    ///
    /// 入队可以发生在工作单元之内；Flush 发生在 <c>work</c> 成功返回之后。
    /// 若 work 内有 <c>[VivUnitOfWork]</c> / 窄事务，提交先于 Flush；
    /// handler 失败不能回滚已经提交的主写入。跨进程原子投递请用发件箱（<c>IVivOutbox</c>）。
    /// </summary>
    public interface IVivLocalEventScope
    {
        /// <summary>
        /// 执行一段会入队本地事件的工作：成功 Flush，失败 Discard 后上抛。
        /// Flush 传 <see cref="CancellationToken.None"/> —— handler 必须跑完。
        /// </summary>
        Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default);

        /// <summary>
        /// <see cref="RunAsync(Func{Task}, CancellationToken)"/> 的带返回值版本。
        /// </summary>
        Task<TResult> RunAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default);
    }
}
