namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 可选的消费端 Inbox —— <c>(ServiceName, MessageId)</c> 唯一，用来把 at-least-once 收成业务侧幂等。
    ///
    /// 不强迫所有消费者使用。需要时在同一条业务事务里调 <see cref="TryAcceptAsync"/>：
    /// 新消息返回 true（已记下），重复投递返回 false（调用方跳过业务）。
    /// 没配数据库时容器解析不到本接口，消费者依赖里它是可选参数。
    /// </summary>
    public interface IVivInbox
    {
        /// <summary>
        /// 尝试记录本服务已处理 <paramref name="messageId"/>。
        /// 必须与业务写处于同一 <c>IVivTransaction</c> 才原子 —— 实现走 <c>ExecuteSqlAsync</c>，
        /// 会并入调用方当前事务。没有外层事务时依然落库（有持久性、没有与业务写的原子性）。
        /// </summary>
        /// <returns>首次接受 true；唯一约束冲突（已处理过）false。</returns>
        Task<bool> TryAcceptAsync(long messageId, CancellationToken cancellationToken = default);
    }
}
