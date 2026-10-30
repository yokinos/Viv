using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Exceptions;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 工作单元实现 —— 只负责「嵌套合并」这一件事。
    ///
    /// 本类是 Scoped，<c>_depth</c> 就是请求作用域的状态。
    /// 被解析到根作用域的话并发请求会共用一个状态机。
    ///
    /// 嵌套：只有最外层真正开事务，内层直接并入外层执行（不重开、不单独提交、不重试）。
    /// 没有保存点。内层抛异常，或内层 <c>commitWhen</c> 返回 false，都会把整笔打成只能回滚：
    /// 前者原样上抛，后者在最外层抛 <see cref="VivUnitOfWorkException"/>，拦截器才能 Discard 本地事件。
    ///
    /// 不用锁 —— 一个作用域内的事务不该被多个线程同时驱动。
    /// </summary>
    internal sealed class UnitOfWorkManager : IVivUnitOfWork
    {
        private readonly ITransactionKernel _kernel;

        /// <summary>
        /// 当前未结束的事务层数。0 = 没有进行中的事务
        /// </summary>
        private int _depth;

        /// <summary>内层拒绝过提交。最外层看到它就回滚并抛，不能把外层的成功返回值交出去。</summary>
        private bool _rollbackOnly;

        public UnitOfWorkManager(ITransactionKernel kernel, ILoggerContract logger)
        {
            _kernel = kernel;

            // 注册期拿不到 logger（VivLocator 还没 Initialize），扫描结论先存静态，
            // 这里首次构造时补一条启动日志。与 NanaLocalEventPublisher 同一套做法。
            UnitOfWorkDiagnostics.LogOnce(logger);
        }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool>? commitWhen = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            // 嵌套：并入外层。提交与否由最外层决定，内层不重试
            if (_depth > 0)
            {
                return ExecuteNestedAsync(work, commitWhen, cancellationToken);
            }

            return _kernel.ExecuteAsync(async innerToken =>
            {
                _depth = 1;
                _rollbackOnly = false;
                try
                {
                    var result = await work(innerToken).ConfigureAwait(false);
                    if (_rollbackOnly)
                    {
                        throw new VivUnitOfWorkException("内层事务已拒绝提交，整笔回滚。");
                    }

                    return result;
                }
                finally
                {
                    // 归零放在 finally：work 抛异常时，同一作用域里后续事务不该带着上一个的残留状态跑
                    _depth = 0;
                }
            }, commitWhen, cancellationToken);
        }

        /// <summary>
        /// 内层把结果交回调用方，拒绝提交只打标记。
        /// 在这里抛的话，外层方法体看不到内层的失败信封。
        /// </summary>
        private async Task<T> ExecuteNestedAsync<T>(
            Func<CancellationToken, Task<T>> work,
            Func<T, bool>? commitWhen,
            CancellationToken cancellationToken)
        {
            _depth++;
            try
            {
                var result = await work(cancellationToken).ConfigureAwait(false);
                if (commitWhen is not null && !commitWhen(result))
                    _rollbackOnly = true;

                return result;
            }
            finally
            {
                _depth--;
            }
        }

        public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            return ExecuteAsync(async ct =>
            {
                await work(ct).ConfigureAwait(false);
                return true;
            }, commitWhen: null, cancellationToken: cancellationToken);
        }
    }
}
