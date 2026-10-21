using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Viv.Elysia.Interface;

namespace Viv.Fakes;

/// <summary>
/// <see cref="IHostEnvironment"/> 替身 —— 默认是「开发环境」。
/// <c>VivExceptionFilterAttribute</c> 靠它决定要不要把异常详情带进响应，
/// 所以想验「生产环境不吐细节」就把 <see cref="EnvironmentName"/> 改掉。
/// </summary>
public class StubHost : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "test";

    public string ContentRootPath { get; set; } = ".";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>
/// <see cref="IApiRequest"/> 替身 —— 请求校验的最小载体，<see cref="Validate"/> 原样返回构造时给的结果
/// （空串 = 校验通过）。<c>RequestFilterAttribute</c> 不空即拦，所以这一个旋钮就够摆了。
/// </summary>
public class FakeRequest : IApiRequest
{
    private readonly string _error;

    public FakeRequest(string error) => _error = error;

    public string Validate() => _error;
}

/// <summary>记录一次对 SignalR 客户端的调用（经 <see cref="IClientProxy.SendCoreAsync"/>）。</summary>
public sealed record ProxyCall(string TargetKind, string TargetId, string Method, object?[] Args);

/// <summary>
/// <c>IHubContext&lt;THub&gt;</c> 替身 —— 把每一次「发给谁」记进 <see cref="Calls"/>。
/// 泛型是为了把「具体 Hub 类型」留在各测试项目里：Fakes 不该认识任何一个业务 Hub。
///
/// <see cref="Clients"/> 的各个重载只实现被测代码真正用到的那几个
/// （<c>All</c> / <c>Client</c> / <c>Clients</c> / <c>Group</c> / <c>Groups</c>），
/// 其余（<c>AllExcept</c> / <c>User</c> / …）保持 <c>NotImplementedException</c> ——
/// 真被走到说明被测代码换了发送目标，那是需要立刻知道的事，不该静默放过。
/// </summary>
public sealed class FakeHubContext<THub> : IHubContext<THub> where THub : Hub
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
    /// <summary>记录加组 (connectionId, groupName)</summary>
    public List<(string ConnectionId, string Group)> Added { get; } = new();

    /// <summary>记录退组 (connectionId, groupName)</summary>
    public List<(string ConnectionId, string Group)> Removed { get; } = new();

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

