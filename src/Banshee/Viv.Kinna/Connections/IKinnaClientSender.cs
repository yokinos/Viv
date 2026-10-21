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
    }
}
