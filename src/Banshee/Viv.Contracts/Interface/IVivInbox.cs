namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 可选的消费端 Inbox —— <c>(ServiceName, 键)</c> 唯一，用来把 at-least-once 收成幂等。
    ///
    /// 不强迫所有消费者使用。需要时在同一条业务事务里调 <c>TryAcceptAsync</c>：
    /// 首次返回 true（已记下），重复返回 false（调用方跳过业务）。
    /// 没配数据库时容器解析不到本接口，消费者依赖里它是可选参数。
    ///
    /// 两个重载管的是两件事，别混。① 收 <c>long</c> 的是消息级去重，键就是 MessageId，
    /// 挡的是「同一条消息被投递两次」—— 发件箱重试、租约过期重投、RedeliverAsync 都算。
    /// ② 收 <c>string</c> 的是业务级幂等，键由调用方给，挡的是「同一件事提交了两次」。
    /// 这两次在投递层看是两条不相干的消息（MessageId 不同，甚至一条来自 MQ、一条来自 HTTP 重试），
    /// 消息级去重对它们完全无感，只有业务键认得出来。
    ///
    /// 键的 scope 是 ServiceName（入口程序集名），租户不在里面 —— 要按租户隔离就把租户拼进业务键。
    /// </summary>
    public interface IVivInbox
    {
        /// <summary>
        /// 尝试记录本服务已处理 <paramref name="messageId"/>。0（空信封）直接返回 false，不落库。
        /// 必须与业务写处于同一 <c>IVivTransaction</c> 才原子 —— 实现走 <c>ExecuteSqlAsync</c>，
        /// 会并入调用方当前事务。没有外层事务时依然落库（有持久性、没有与业务写的原子性）。
        /// </summary>
        /// <returns>首次接受 true；唯一约束冲突（已处理过）false。</returns>
        Task<bool> TryAcceptAsync(long messageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试记录本服务已处理过业务键 <paramref name="idempotentKey"/>：同一件事第二次提交返回 false。
        /// 空或全空白键直接返回 false，不落库。
        ///
        /// 键要能唯一标识「一件事」—— 客户端请求号、订单号加动作、流水号都行，什么粒度由调用方定。
        /// 框架不加工键的内容，也不替你带租户：多租户下记得自己拼进去（如 <c>$"{tenantId}:{orderId}"</c>），
        /// 否则两个租户用了同一个键时，后一个的正当业务会被静默当成重复跳过。
        /// 存储上限 200 字符，超了由数据库直接报错，不截断。
        ///
        /// 必须包在事务里用（HTTP 侧标 <c>[VivUnitOfWork]</c>）。没有外层事务时这行插入立刻落库，
        /// 业务随后失败也不会撤销 —— 这个键就被永久标成「处理过」，客户端重试全被拒，
        /// 而现象看起来像是「系统说他处理过了」。
        /// </summary>
        /// <returns>首次接受 true；唯一约束冲突（已处理过）false。</returns>
        Task<bool> TryAcceptAsync(string idempotentKey, CancellationToken cancellationToken = default);
    }
}
