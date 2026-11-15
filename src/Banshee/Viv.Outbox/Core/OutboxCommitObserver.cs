using Viv.Contracts.Interface;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 把「事务提交」接到投递器的唤醒信号上：本作用域入过队、且事务真的提交了，才叫醒投递器。
    ///
    /// 为什么不直接在 <see cref="OutboxStore.EnqueueAsync"/> 里 Notify：那个方法明确要求
    /// 「必须在事务内调用」（行还没提交），此刻叫醒投递器，它认领不到任何东西，
    /// 白跑一轮然后继续睡 —— 等于没优化。
    ///
    /// Scoped：与 <see cref="OutboxStore"/>、<c>IMomoDbContext</c> 同作用域。
    /// 标记只关心"这个作用域里有没有成功入过队"，用 0/1 而不是计数 —— 一轮提交叫醒一次就够。
    ///
    /// 已知取舍：只实现 <see cref="ITransactionObserver"/>、不认识 <c>OnAttemptFailed</c>，
    /// 所以"入队成功但事务回滚"之后标记不会被清，同一作用域里下一次成功提交会多叫醒一次。
    /// 代价是一轮认领查询、认 0 行；要消掉它得让 <c>ITransactionAttemptListener</c> 支持多实现，
    /// 那个改动的面比这里的收益大，不做。
    /// </summary>
    internal sealed class OutboxCommitObserver : ITransactionObserver
    {
        private readonly OutboxSignal _signal;

        /// <summary>本作用域内是否已成功入队（0/1 而非计数，见类型注释）</summary>
        private int _pending;

        public OutboxCommitObserver(OutboxSignal signal)
        {
            _signal = signal;
        }

        /// <summary>
        /// 入队成功后调一次。由 <see cref="OutboxStore"/> 在确认影响行数 &gt; 0 时调用 ——
        /// 入队没生效时不能标记，否则会叫醒一轮空投递。
        /// </summary>
        public void MarkPending() => Interlocked.Exchange(ref _pending, 1);

        /// <inheritdoc />
        public void OnCommitted()
        {
            // 交换而不是读取：一次提交只该消费掉一次标记，重复回调（多事务嵌套收尾）不重复唤醒
            if (Interlocked.Exchange(ref _pending, 0) == 0)
            {
                return;
            }

            _signal.Notify();
        }
    }
}
