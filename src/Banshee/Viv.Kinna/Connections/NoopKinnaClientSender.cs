namespace Viv.Kinna.Connections
{
    /// <summary>
    /// <see cref="Options.KinnaOptions.EnableSignalR"/> 关掉时顶替 <see cref="KinnaClientSender{THub}"/> 的位置。
    ///
    /// 存在的意义只有一个：让 <see cref="KinnaSession{THub}"/> 的构造不再解析
    /// <c>IHubContext&lt;THub&gt;</c>。分流判断在 <see cref="WebSockets.CompositeKinnaClientSender{THub}"/> 里
    /// 已经做过一次，正常情况下这里的方法不会被调用——留着完整实现是为了「万一被调到也只是空操作」，
    /// 而不是抛异常把调用方带崩。
    /// </summary>
    internal sealed class NoopKinnaClientSender : IKinnaClientSender
    {
        public static NoopKinnaClientSender Instance { get; } = new();

        public Task SendToConnectionAsync(
            string connectionId,
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendToConnectionsAsync(
            IReadOnlyList<string> connectionIds,
            string method,
            object? payload,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendToGroupAsync(
            string groupName,
            string method,
            object? payload,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TerminateConnectionAsync(
            string connectionId,
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
