using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Contracts;

namespace Viv.Kinna.Ports
{
    /// <summary>
    /// 跨副本控制指令的广播通道。框架不引 Redis、不引消息队列，
    /// 实现留在宿主（Herta 用 Redis pub/sub）。
    ///
    /// 只用于控制面：踢人、强制下线、配置变更通知。
    /// 不要把连接路由或连接状态塞进来 —— 投递走的是「每副本收全量事件、
    /// 只推本机连接」，一旦路由进了缓存，一个 Redis 抖动就会变成「连不上」。
    /// </summary>
    public interface IKinnaKickBus<THub> where THub : Hub
    {
        /// <summary>
        /// 本实例标识。发布时写进 <see cref="KinnaKickCommand.Origin"/>，
        /// 订阅端据此跳过自己那份，避免同一次踢人执行两遍
        /// </summary>
        string InstanceId { get; }

        /// <summary>
        /// 广播一条指令给所有副本（含本机）。
        /// 实现不应抛异常给调用方之外的路径：本机已经踢过了，广播失败只影响其余副本的时效。
        ///
        /// <paramref name="cancellationToken"/> 只保证「还没发就别发」：底层的发布调用
        /// （如 Redis pub/sub）未必支持取消，发出去之后就不再打断。
        /// </summary>
        Task PublishAsync(KinnaKickCommand command, CancellationToken cancellationToken = default);

        /// <summary>
        /// 订阅广播，返回的 <see cref="IDisposable"/> 释放即退订。
        ///
        /// 实现**必须**保证同一时刻只有一个回调在跑（下发线程上只入队，另起单读者串行处理）：
        /// 订阅端拿到的是共享的协调器，两条指令并发进来会同时摘同一批连接、
        /// 软踢帧交错写进待发队列。调用方可以按「回调不并发」来写。
        /// </summary>
        IDisposable Subscribe(Func<KinnaKickCommand, Task> handler);
    }
}
