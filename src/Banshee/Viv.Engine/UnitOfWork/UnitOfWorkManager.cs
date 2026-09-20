using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 工作单元实现，用于嵌套调用场景，同一作用域内全程仅持有单个数据库事务。
    /// <list type="bullet">
    /// <item><description>生命周期：Scoped 注册。_depth、_rollbackOnly 为请求作用域内状态；若注册为根作用域，多请求会共享状态机 —— 一个请求提交会把另一个请求的事务也提交掉。</description></item>
    /// <item><description>嵌套规则：仅最外层句柄创建与提交真实事务；嵌套层拿到虚拟子句柄。</description></item>
    /// <item><description>粘性回滚：任意子句柄未正常提交释放，整个作用域标记 rollback-only；即使外层执行提交，最终仍回滚并记录警告，不使用数据库保存点。</description></item>
    /// <item><description>并发约定：无内置锁。同一作用域禁止多线程并发操作事务，不允许 Task.WhenAll / Parallel.* / 新建线程执行数据库操作；如需多线程支持，需增加锁或改用 AsyncLocal 存储状态。</description></item>
    /// <item><description>状态重置：作用域内可顺序执行多组事务；最外层事务完成后，深度、回滚标记全部清零，上一轮失败状态不会污染后续事务。</description></item>
    /// </list>
    /// </summary>
    internal sealed class UnitOfWorkManager : IVivUnitOfWork
    {
        private readonly ITransactionKernel _kernel;
        private readonly ILoggerContract _logger;

        /// <summary>
        /// 当前未结束的句柄数。0 = 没有进行中的事务
        /// </summary>
        private int _depth;

        /// <summary>
        /// 粘性回滚标记 —— 置位后最外层也只能回滚
        /// </summary>
        private bool _rollbackOnly;

        public UnitOfWorkManager(ITransactionKernel kernel, ILoggerContract logger)
        {
            _kernel = kernel;
            _logger = logger;

            // 注册期拿不到 logger（VivLocator 还没 Initialize），扫描结论先存静态，
            // 这里首次构造时补一条启动日志。与 NanaLocalEventPublisher 同一套做法。
            UnitOfWorkDiagnostics.LogOnce(logger);
        }

        public async Task<IVivTransaction> BeginAsync(CancellationToken cancellationToken = default)
        {
            var isRoot = _depth == 0;

            if (isRoot)
            {
                // 先开事务、再记账 —— 开失败就退出，不留半截状态
                var ok = await _kernel.BeginAsync(cancellationToken).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException("数据库事务开启失败");
                }
            }

            _depth++;
            return new TransactionHandle(this, isRoot);
        }

        /// <summary>
        /// 句柄结束时回头找管理者结账。只有最外层那个句柄会碰到数据库。
        /// </summary>
        internal async Task EndAsync(bool isRoot, bool commitRequested, CancellationToken cancellationToken)
        {
            if (_depth > 0) _depth--;

            if (!isRoot)
            {
                // 子句柄：不提交、不回滚，只负责「没提交就结束」这件事的后果
                if (!commitRequested) _rollbackOnly = true;
                return;
            }

            var effectiveCommit = commitRequested && !_rollbackOnly;

            if (commitRequested && _rollbackOnly)
            {
                // 不静默 —— 业务以为提交了、实际回滚了，是排查成本最高的一类问题
                _logger.Warning("嵌套事务已标记回滚，最外层的提交被降级为回滚");
            }

            // 状态先归零：无论成败，这个事务都结束了。
            // 放到 await 之前而不是 finally 之后 —— 提交抛异常时状态同样要清干净，
            // 否则同一作用域里后续的事务会带着上一个的残留标记跑。
            _rollbackOnly = false;

            if (effectiveCommit)
            {
                // 提交失败必须响亮（Momo 会包成 VivConnectionException 抛出来），不吞
                await _kernel.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await SafeRollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 回滚失败只记日志不抛 —— 与 <c>MomoDatabase.RollbackTransaction</c> 同一取舍：
        /// 回滚通常是异常路径上的补救动作，此刻再抛会掩盖真正触发回滚的那个异常。
        /// </summary>
        private async Task SafeRollbackAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _kernel.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("事务回滚失败（原始异常优先，此处只记日志）", ex);
            }
        }

        /// <summary>
        /// 窄事务句柄。调用方拿到的就是它，<c>await using</c> 离开作用域时若未提交则自动回滚。
        /// </summary>
        private sealed class TransactionHandle : IVivTransaction
        {
            private readonly UnitOfWorkManager _manager;
            private readonly bool _isRoot;
            private bool _completed;

            public TransactionHandle(UnitOfWorkManager manager, bool isRoot)
            {
                _manager = manager;
                _isRoot = isRoot;
            }

            public Task CommitAsync(CancellationToken cancellationToken = default)
                => CompleteAsync(commitRequested: true, cancellationToken);

            /// <summary>
            /// 回滚。粘性由 <c>EndAsync</c> 在非最外层分支上打标记保证。
            /// 这里不预先打标记：本方法在已完成的句柄上应当完全无效，提前打标记会把
            /// 「已经提交完的事务」的状态泄漏给同作用域里的下一个事务 —— 下一个会静默变成回滚。
            /// </summary>
            public Task RollbackAsync(CancellationToken cancellationToken = default)
                => CompleteAsync(commitRequested: false, cancellationToken);

            /// <summary>幂等 —— <c>await using</c> 里显式提交后再释放不会重复动作</summary>
            private async Task CompleteAsync(bool commitRequested, CancellationToken cancellationToken)
            {
                if (_completed) return;
                _completed = true;
                await _manager.EndAsync(_isRoot, commitRequested, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// 同步释放路径，只在调用方写 <c>using</c>（无 await）时兜底；正常走 <see cref="DisposeAsync"/>。
            /// 阻塞等待是刻意的 —— 这里唯一的工作是一次回滚，ASP.NET Core 无同步上下文，不存在死锁场景。
            /// </summary>
            public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

            public ValueTask DisposeAsync()
                => new(CompleteAsync(commitRequested: false, CancellationToken.None));
        }
    }
}
