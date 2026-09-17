using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 工作单元实现 —— 把「嵌套调用」和「只有一个事务」这两件事对上。
    ///
    /// 【作用域铁律】
    /// 本类是 Scoped，事务状态（<c>_depth</c> / <c>_rollbackOnly</c>）就是<b>请求作用域的状态</b>。
    /// 一旦被解析到根作用域，并发请求会共用同一个状态机 —— 一个请求提交会把另一个请求的事务也提交掉。
    /// 已实测 Autofac 的接口代理从<b>当前</b>作用域解析拦截器（不落根），此前提成立。
    ///
    /// 【嵌套语义：只有最外层开 / 提交，没有保存点】
    /// <code>
    /// [VivUnitOfWork]                  // ← 最外层：真开事务
    /// public async Task OuterAsync()
    /// {
    ///     await using var tx = await _uow.BeginAsync();   // ← 内层：子句柄，不碰数据库
    ///     await InnerAsync();                             //   它的 Commit 是空操作
    ///     await tx.CommitAsync();
    /// }
    /// </code>
    /// 子句柄没提交就结束 → 整个作用域被标记 <b>rollback-only（粘性）</b>，
    /// 最外层再调 <c>CommitAsync</c> 也会被降级成回滚，并记一条 Warning。
    ///
    /// 【不做并发保护】本类不用锁 —— 一个作用域内的事务本就不该被多个线程同时驱动。
    /// 全仓已确认无 <c>Task.WhenAll</c> / <c>Parallel.*</c> / <c>new Thread</c> 做数据库操作。
    /// 若将来出现，并发进入 <c>BeginAsync</c> 会让 <c>_depth</c> 竞争 —— 那时再加锁或改为 AsyncLocal。
    ///
    /// 【单次 vs 复用】一个作用域内可以<b>顺序</b>跑完多个事务（前一个结束了再开下一个）：
    /// 最外层结束时状态归零，<c>_rollbackOnly</c> 一并清掉，不会把上一个事务的失败带给下一个。
    /// </summary>
    internal sealed class UnitOfWorkManager : IVivUnitOfWork
    {
        private readonly ITransactionKernel _kernel;
        private readonly ILoggerContract _logger;

        /// <summary>当前未结束的句柄数。0 = 没有进行中的事务</summary>
        private int _depth;

        /// <summary>粘性回滚标记 —— 置位后最外层也只能回滚</summary>
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

        /// <summary>句柄结束时回头找管理者结账。只有最外层那个句柄会碰到数据库。</summary>
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
            /// 回滚。粘性由 <c>EndAsync</c> 在非最外层分支上打标记保证（没有保存点，子句柄回滚会把整个事务拖下水）。
            /// <b>这里刻意不预先打标记</b>：本方法在已完成的句柄上被调用时应当完全无效，
            /// 提前打标记会把「已经提交完的事务」的状态泄漏给同作用域里的下一个事务 —— 下一个事务会静默变成回滚。
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
            /// 同步释放路径。正常业务用 <c>await using</c> 走 <see cref="DisposeAsync"/>，
            /// 这里只在调用方写 <c>using</c>（无 await）时兜底。
            /// 阻塞等待是刻意的：本方法唯一的工作是一次回滚，ASP.NET Core 无同步上下文，
            /// 不存在经典的死锁场景。若调用方不在 async 流程里，这是唯一能做完回滚的时机。
            /// </summary>
            public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

            public ValueTask DisposeAsync()
                => new(CompleteAsync(commitRequested: false, CancellationToken.None));
        }
    }
}
