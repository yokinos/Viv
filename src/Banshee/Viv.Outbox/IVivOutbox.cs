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
        /// 没有事务时依然落库（有持久性、没有原子性）。不自动开事务是刻意的 ——
        /// 每次入队各开各的事务，等于把一条完整业务拆成两个互不相干的提交。
        /// </summary>
        /// <typeparam name="T">必须继承 <see cref="NanaEvent"/></typeparam>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>content 为 null 时 false；落库成功 true。数据库故障抛连接异常。</returns>
        Task<bool> EnqueueAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent;
    }
}
