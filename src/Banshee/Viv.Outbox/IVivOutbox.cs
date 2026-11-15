using Viv.Nana;

namespace Viv.Outbox
{
    /// <summary>
    /// 发件箱 —— 事务性消息投递。把待发消息与业务写放进同一个本地事务，一个提交两个都成立；
    /// 真正发出去交给后台投递器（<c>OutboxDispatcher</c>）。
    ///
    /// 投递是 at-least-once：投递器崩溃 / 租约过期会让同一条消息被投递两次。
    /// 消费端去重靠 <c>MessageId</c>；需要与业务写同事务的幂等时用可选的 <c>IVivInbox</c>。
    /// </summary>
    public interface IVivOutbox
    {
        /// <summary>
        /// 把事件写进发件箱 —— 不发送任何东西。
        /// 要原子就必须与业务写处于同一事务：调用方自己 <c>BeginAsync</c> 划线，在提交之前调本方法。
        ///
        /// 不在事务中直接抛，而不是"照样落库"。不自动开事务是刻意的 ——
        /// 每次入队各开各的事务，等于把一条完整业务拆成两个互不相干的提交。
        /// 确实不该单独立事务的场景用 <see cref="EnqueueWithoutTransactionAsync{T}"/>。
        /// </summary>
        /// <typeparam name="T">必须继承 <see cref="NanaEvent"/></typeparam>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>content 为 null 时 false；落库成功 true。数据库故障抛连接异常。</returns>
        Task<bool> EnqueueAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent;

        /// <summary>
        /// 把事件写进发件箱，<b>不要求</b>处于事务中 —— 显式放弃「与业务写同生共死」这条保证。
        ///
        /// 什么时候该用它：这次入队背后没有需要一起成立的业务写 —— 补偿脚本、离线补数据、
        /// 幂等重放（业务写早已提交，只是事件没发出去）。这类场景用 <see cref="EnqueueAsync{T}"/>
        /// 只能靠包一个空事务绕过断言，那反而让"事务"这个词失去意义。
        ///
        /// 语义差别只有一处：没有外层事务时语句自动提交，落库那一刻即可投递，
        /// 所以会立刻唤醒投递器，不走"等提交回调"那条路。
        /// 若调用时恰好已经开着事务，行为与 <see cref="EnqueueAsync{T}"/> 完全一致，仍随该事务一起提交、一起回滚。
        ///
        /// 代价：业务写后来失败时这条消息不会被撤回。选它之前先确认自己不需要这个保证 ——
        /// 名字长成这样就是为了在 code review 里一眼被看见。
        /// </summary>
        /// <typeparam name="T">必须继承 <see cref="NanaEvent"/></typeparam>
        /// <returns>content 为 null 时 false；落库成功 true。数据库故障抛连接异常。</returns>
        Task<bool> EnqueueWithoutTransactionAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent;
    }
}
