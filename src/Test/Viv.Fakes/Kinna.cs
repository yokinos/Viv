using Microsoft.AspNetCore.SignalR;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Dispatch;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;

namespace Viv.Fakes;

/// <summary>记录一次下行发送（经 <see cref="IKinnaClientSender"/>）。</summary>
public sealed record SenderCall(string TargetKind, string TargetId, string Method, object?[] Args);

/// <summary>发送口替身：记下发给了谁、发的是什么。</summary>
public sealed class RecordingKinnaClientSender : IKinnaClientSender
{
    public List<SenderCall> Calls { get; } = new();

    public List<(string ConnectionId, string Group)> Added { get; } = new();

    public List<(string ConnectionId, string Group)> Removed { get; } = new();

    /// <summary>设了就抛，用于测发送失败</summary>
    public Exception? ThrowOnSend { get; set; }

    public Task SendToConnectionAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend != null) throw ThrowOnSend;

        Calls.Add(new SenderCall("Client", connectionId, method, args));
        return Task.CompletedTask;
    }

    public Task SendToConnectionsAsync(IReadOnlyList<string> connectionIds, string method, object? payload, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend != null) throw ThrowOnSend;

        Calls.Add(new SenderCall("Clients", string.Join(",", connectionIds), method, [payload]));
        return Task.CompletedTask;
    }

    public Task SendToGroupAsync(string groupName, string method, object? payload, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend != null) throw ThrowOnSend;

        Calls.Add(new SenderCall("Group", groupName, method, [payload]));
        return Task.CompletedTask;
    }

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    /// <summary>软踢 + 断开。和普通发送记在同一个列表里，调用方断言方式不变。</summary>
    public Task TerminateConnectionAsync(string connectionId, string method, object?[] args, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend != null) throw ThrowOnSend;

        Calls.Add(new SenderCall("Client", connectionId, method, args));
        return Task.CompletedTask;
    }
}

/// <summary>可预设返回值的连接表替身，记录最近一次查询参数。参数顺序是 AppId、SubjectId、UserId。</summary>
public sealed class StubKinnaConnectionPool : IKinnaConnectionPool
{
    public List<string> ConnectionIds { get; set; } = new();

    public List<KinnaConnectionInfo> Connections { get; set; } = new();

    public long? LastSubjectId { get; private set; }

    public long? LastUserId { get; private set; }

    public void Add(string connectionId, long appId, long subjectId, long userId) { }

    public KinnaConnectionInfo? Remove(string connectionId) => Info;

    public List<string> GetConnectionIds(long subjectId, long userId)
    {
        LastSubjectId = subjectId;
        LastUserId = userId;
        return ConnectionIds;
    }

    public List<string> GetConnectionIds(long appId, long subjectId, long userId) => ConnectionIds;

    public List<KinnaConnectionInfo> GetConnections(long subjectId) => Connections;

    /// <summary>TryGet 的返回值</summary>
    public KinnaConnectionInfo? Info { get; set; }

    public KinnaConnectionInfo? TryGet(string connectionId) => Info;

    public void Touch(string connectionId) { }

    public List<KinnaConnectionInfo> SweepExpired(TimeSpan ttl) => new();

    /// <summary>健康检查看到的快照，由用例指定</summary>
    public KinnaConnectionPoolSnapshot Snapshot { get; set; }

    public KinnaConnectionPoolSnapshot GetSnapshot() => Snapshot;

    public void Clear() { }
}

/// <summary>一个 Hub 的运行实例替身。</summary>
public sealed class StubKinnaSession<THub> : IKinnaSession<THub> where THub : Hub
{
    public StubKinnaSession(
        IKinnaConnectionPool? pool = null,
        IKinnaClientSender? sender = null,
        IKinnaKeys? keys = null,
        KinnaOptions? options = null)
    {
        Pool = pool ?? new StubKinnaConnectionPool();
        Sender = sender ?? new RecordingKinnaClientSender();
        Keys = keys ?? new KinnaKeys("kinna");
        Options = options ?? new KinnaOptions();
    }

    public string Hub => typeof(THub).Name;

    public IKinnaConnectionPool Pool { get; }

    public IKinnaClientSender Sender { get; }

    public IKinnaKeys Keys { get; }

    public KinnaOptions Options { get; }
}

/// <summary>推送计划替身：返回什么由测试给，也可以让它抛。</summary>
public sealed class FakeKinnaPusher<TEvent> : IKinnaPusher<TEvent>
{
    public Func<TEvent, KinnaPushContext?, KinnaPushPlan?>? OnPlan { get; set; }

    public Exception? ThrowOnPlan { get; set; }

    public int Calls { get; private set; }

    public KinnaPushPlan? Plan(TEvent evt, KinnaPushContext? context)
    {
        Calls++;
        if (ThrowOnPlan != null) throw ThrowOnPlan;
        return OnPlan?.Invoke(evt, context);
    }
}

/// <summary>可预设群列表的 <see cref="IKinnaGroupSource{THub}"/> 替身，并记录最近一次查询参数。</summary>
public sealed class StubKinnaGroupSource<THub> : IKinnaGroupSource<THub> where THub : Hub
{
    public List<long> GroupIds { get; set; } = new();

    /// <summary>设了就抛，用于测群服务故障时连接是否照常建立</summary>
    public Exception? ThrowOnGet { get; set; }

    public long? LastSubjectId { get; private set; }

    public long? LastUserId { get; private set; }

    public Task<List<long>> GetUserGroupIdsAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
    {
        LastSubjectId = subjectId;
        LastUserId = userId;

        if (ThrowOnGet != null) throw ThrowOnGet;

        return Task.FromResult(GroupIds);
    }
}

/// <summary>投递确认存储替身：逐条记 Id，超过上限只留水位，行为对齐 Redis 实现。</summary>
public sealed class StubKinnaDeliveryStore<THub> : IKinnaDeliveryStore<THub> where THub : Hub
{
    public long? Watermark { get; set; }

    public List<long> MessageIds { get; } = new();

    public int MaxMessageIds { get; set; } = 200;

    public List<(long SubjectId, long UserId, long[] MessageIds)> Acks { get; } = new();

    /// <summary>设了就抛，用于测落库失败时的降级</summary>
    public Exception? ThrowOnAck { get; set; }

    /// <summary>设了就抛，用于测 Resync 读失败</summary>
    public Exception? ThrowOnGet { get; set; }

    /// <summary>设了就等它完成才返回，用于测队头阻塞（慢 Ack 会不会拖住心跳）</summary>
    public Task? AckGate { get; set; }

    public async Task<long> AckAsync(long subjectId, long userId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAck != null) throw ThrowOnAck;

        if (AckGate != null)
        {
            await AckGate.ConfigureAwait(false);
        }

        Acks.Add((subjectId, userId, [.. messageIds]));

        foreach (var id in messageIds.Where(x => !MessageIds.Contains(x)))
        {
            MessageIds.Add(id);
        }

        if (messageIds.Count > 0)
            Watermark = Math.Max(Watermark ?? 0, messageIds.Max());

        // 记满就全清，只留水位
        if (MessageIds.Count > MaxMessageIds)
            MessageIds.Clear();

        return Watermark ?? 0;
    }

    public Task<KinnaDeliveryState> GetDeliveredAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnGet != null) throw ThrowOnGet;

        return Task.FromResult(new KinnaDeliveryState(Watermark, [.. MessageIds]));
    }
}

/// <summary>在线态端口替身：三个动作都记在案。</summary>
public sealed class StubKinnaPresenceSink<THub> : IKinnaPresenceSink<THub> where THub : Hub
{
    public List<(long SubjectId, long UserId)> OnlineUsers { get; } = new();

    public List<(long SubjectId, long UserId)> OfflineUsers { get; } = new();

    public List<(long SubjectId, long UserId)> RenewedUsers { get; } = new();

    public Task OnlineAsync(long subjectId, long userId)
    {
        OnlineUsers.Add((subjectId, userId));
        return Task.CompletedTask;
    }

    public Task OfflineAsync(long subjectId, long userId)
    {
        OfflineUsers.Add((subjectId, userId));
        return Task.CompletedTask;
    }

    public Task RenewAsync(long subjectId, long userId)
    {
        RenewedUsers.Add((subjectId, userId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// 跨副本踢人通道替身。既能记录「发布出去什么」，也能手动投递一条
/// 「来自别的副本」的指令，用来单独测订阅端，不需要真起 Redis。
/// </summary>
public sealed class RecordingKinnaKickBus<THub> : IKinnaKickBus<THub> where THub : Hub
{
    private readonly List<Func<KinnaKickCommand, Task>> _handlers = [];

    private readonly TaskCompletionSource<bool> _subscribed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string InstanceId { get; set; } = "self";

    /// <summary>本实例发布出去的指令，按顺序</summary>
    public List<KinnaKickCommand> Published { get; } = [];

    /// <summary>设了就抛，用于测「广播失败不影响本机踢人」</summary>
    public Exception? ThrowOnPublish { get; set; }

    /// <summary>当前仍有效的订阅数，用来验证 Dispose 真的退订了</summary>
    public int SubscriberCount => _handlers.Count;

    /// <summary>
    /// 等订阅登记完成。宿主 <c>StartAsync</c> 返回时 <c>ExecuteAsync</c> 不一定已经跑到订阅那一步
    /// （.NET 10 里它不再保证同步），测试直接断言订阅数会偶发为 0
    /// </summary>
    public Task WhenSubscribedAsync(TimeSpan timeout) => _subscribed.Task.WaitAsync(timeout);

    public Task PublishAsync(KinnaKickCommand command, CancellationToken cancellationToken = default)
    {
        if (ThrowOnPublish != null) throw ThrowOnPublish;

        // 真实实现会用自己的实例 Id 补 Origin，替身保持一致，测试才能覆盖「跳过自己」
        command.Origin ??= InstanceId;
        Published.Add(command);
        return Task.CompletedTask;
    }

    public IDisposable Subscribe(Func<KinnaKickCommand, Task> handler)
    {
        _handlers.Add(handler);
        _subscribed.TrySetResult(true);
        return new Unsubscriber(_handlers, handler);
    }

    /// <summary>模拟收到一条广播，所有订阅者依次执行</summary>
    public async Task DeliverAsync(KinnaKickCommand command)
    {
        foreach (var handler in _handlers.ToArray())
        {
            await handler(command);
        }
    }

    private sealed class Unsubscriber(
        List<Func<KinnaKickCommand, Task>> handlers,
        Func<KinnaKickCommand, Task> handler) : IDisposable
    {
        public void Dispose() => handlers.Remove(handler);
    }
}
