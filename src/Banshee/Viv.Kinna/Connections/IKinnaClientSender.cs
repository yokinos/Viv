namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 下行发送口。业务代码只通过它发数据，不直接碰 Hub 类型。
    ///
    /// SignalR 的 IHubContext&lt;THub&gt; 只能按具体类型解析，所以 <c>THub</c> 在注册时定下来，
    /// 由 <see cref="KinnaClientSender{THub}"/> 落到对应的 Hub 上。
    /// </summary>
    public interface IKinnaClientSender
    {
        Task SendToConnectionAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default);

        Task SendToConnectionsAsync(IReadOnlyList<string> connectionIds, string method, object? payload, CancellationToken cancellationToken = default);

        Task SendToGroupAsync(string groupName, string method, object? payload, CancellationToken cancellationToken = default);

        Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

        Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 软踢并断开一条连接：先把 <paramref name="method"/> 那一帧送出去，再让传输层真正关掉它。
        ///
        /// 和 <see cref="SendToConnectionAsync"/> 的差别在最后一步。SignalR 没有服务端硬断的 API，
        /// 只能发帧、指望客户端自己 stop；原生 WebSocket 的 socket 在服务端手里，帧发完就断。
        /// 只发帧不断开的话，客户端不配合（冻结、忽略未知帧）就会一直留在
        /// 「连接表里没有它、推送永远到不了、它自己还以为连着」的黑洞状态。
        /// </summary>
        Task TerminateConnectionAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default);
    }
}
