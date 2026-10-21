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

    public long? LastSubjectId { get; private set; }

    public long? LastUserId { get; private set; }

    public Task<List<long>> GetUserGroupIdsAsync(long subjectId, long userId)
    {
        LastSubjectId = subjectId;
        LastUserId = userId;
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

    public Task<long> AckAsync(long subjectId, long userId, IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default)
    {
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

        return Task.FromResult(Watermark ?? 0);
    }

    public Task<KinnaDeliveryState> GetDeliveredAsync(long subjectId, long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(new KinnaDeliveryState(Watermark, [.. MessageIds]));
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
