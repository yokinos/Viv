using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Herta.Link.Hubs;
using Viv.Log;
using Viv.Nana;
using Viv.Nana.Core;

namespace Viv.Herta.Tests
{
    /// <summary>记录一次对 SignalR 客户端的调用（经 IClientProxy.SendCoreAsync）。</summary>
    public sealed record ProxyCall(string TargetKind, string TargetId, string Method, object?[] Args);

    public sealed class FakeHubContext : IHubContext<ChatHub>
    {
        public List<ProxyCall> Calls { get; } = new();

        public IHubClients Clients { get; }

        public IGroupManager Groups { get; } = new FakeGroupManager();

        public FakeHubContext() => Clients = new FakeHubClients(Calls);
    }

    public sealed class FakeHubClients : IHubClients
    {
        private readonly List<ProxyCall> _calls;

        public FakeHubClients(List<ProxyCall> calls) => _calls = calls;

        public IClientProxy All => new FakeClientProxy("All", "", _calls);

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds)
            => throw new NotImplementedException();

        public IClientProxy Client(string connectionId) => new FakeClientProxy("Client", connectionId, _calls);

        public IClientProxy Clients(IReadOnlyList<string> connectionIds)
            => new FakeClientProxy("Clients", string.Join(",", connectionIds), _calls);

        public IClientProxy Group(string groupName) => new FakeClientProxy("Group", groupName, _calls);

        public IClientProxy Groups(IReadOnlyList<string> groupNames)
            => new FakeClientProxy("Groups", string.Join(",", groupNames), _calls);

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds)
            => throw new NotImplementedException();

        public IClientProxy User(string userId) => throw new NotImplementedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotImplementedException();
    }

    public sealed class FakeClientProxy : IClientProxy
    {
        private readonly string _kind;
        private readonly string _target;
        private readonly List<ProxyCall> _calls;

        public FakeClientProxy(string kind, string target, List<ProxyCall> calls)
        {
            _kind = kind;
            _target = target;
            _calls = calls;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _calls.Add(new ProxyCall(_kind, _target, method, args));
            return Task.CompletedTask;
        }
    }

    public sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public sealed class FakeLogger : ILoggerContract
    {
        public void Info(string message, params object[] args) { }
        public void Error(string message, Exception ex, params object[] args) { }
        public void Error(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Fatal(string message, params object[] args) { }
        public void Fatal(string message, Exception ex, params object[] args) { }
    }

    /// <summary>可预设 GetConnectionIds 返回值的连接池替身，并记录最近一次查询参数。</summary>
    public sealed class StubConnectionPool : IConnectionPool
    {
        public List<string> ConnectionIds { get; set; } = new();

        public long? LastTenantId { get; private set; }

        public long? LastUserId { get; private set; }

        public void Add(string connectionId, long tenantId, long userId, long appId) { }

        public void Remove(string connectionId) { }

        public List<string> GetConnectionIds(long tenantId, long userId)
        {
            LastTenantId = tenantId;
            LastUserId = userId;
            return ConnectionIds;
        }

        public List<string> GetConnectionIds(long tenantId, long userId, long appId) => ConnectionIds;

        public List<ConnectionInfo> GetConnections(long tenantId) => new();

        public Task ForceDisconnectAsync(string connectionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ForceDisconnectUserAsync(long tenantId, long userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ForceDisconnectTenantAsync(long tenantId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Clear() { }
    }

    public sealed class FakeEventPublisher : IVivEventPublisher
    {
        public List<NanaEvent> Published { get; } = new();

        public Task<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent
        {
            Published.Add(content);
            return Task.FromResult(true);
        }

        public Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default)
            where T : NanaEvent
            => throw new NotImplementedException();

        public Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default)
            where T : NanaEvent
            => throw new NotImplementedException();
    }

    public sealed class FakeContext : IVivContext
    {
        public long AppId => throw new NotImplementedException();

        public long SubjectId => throw new NotImplementedException();

        public long UserId => throw new NotImplementedException();

        public string TraceId => throw new NotImplementedException();

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public VivContextContent? GetRawSnapshot()
        {
            throw new NotImplementedException();
        }

        public void SetSnapshot(VivContextContent model)
        {
            throw new NotImplementedException();
        }
    }
}
