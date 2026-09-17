using Viv.Nana;

namespace Viv.Outbox
{
    /// <summary>
    /// 发件箱 —— 事务性消息投递。把「待发消息」与业务写放进<b>同一个本地事务</b>，
    /// 一个提交两个都成立；真正发出去交给后台投递器（<c>OutboxDispatcher</c>）。
    /// <para>
    /// ⚠️ <b>投递是 at-least-once</b>：投递器崩溃 / 租约过期会让同一条消息被投递两次。
    /// 消费端去重靠 <c>MessageId</c>（<c>nana:{ServiceName}:{EventType}:{MessageId}</c> 消费锁），
    /// 信封原样重发时 MessageId 保持不变。框架<b>不做 Inbox</b>，业务侧若要更强的幂等请自理。
    /// </para>
    /// </summary>
    public interface IVivOutbox
    {
        /// <summary>
        /// 把事件写进发件箱 —— <b>不发送任何东西</b>。
        /// <para>
        /// 要原子就必须与业务写处于同一事务：调用方自己用
        /// <c>await using var tx = await _unitOfWork.BeginAsync()</c> 划线，
        /// 在 <c>tx.CommitAsync()</c> 之前调本方法。
        /// </para>
        /// <para>
        /// <b>没有事务时依然落库</b>（有持久性、没有原子性）—— 不自动开事务是刻意的：
        /// 每次入队各开各的事务，等于把「一条完整业务」拆成两个互不相干的提交，
        /// 恰好废掉这个模式唯一的产出。
        /// </para>
        /// </summary>
        /// <typeparam name="T">必须继承 <see cref="NanaEvent"/>（复用既有的 fanout 拓扑，零新增路由约定）</typeparam>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>content 为 null 时 false；落库成功 true。数据库故障抛连接异常。</returns>
        Task<bool> EnqueueAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent;
    }
}
