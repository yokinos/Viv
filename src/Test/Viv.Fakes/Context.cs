using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Fakes;

/// <summary>
/// <see cref="IVivContext"/> 的内存替身 —— 原本散在 6 个测试项目里的副本合并成一个。
///
/// 取的是<b>快照可写</b>那一版（原来 Outbox 的 <c>StubContext</c> / Momo 的 <c>MutableVivContext</c>
/// 就是这样），因为另外几版「属性 getter 直接抛 NotImplementedException」只是为了让编译通过 ——
/// 它们要表达的「被测代码不该读上下文」改成 <see cref="ThrowOnMemberAccess"/> 显式开关：
/// 需要时打开就是断言，不需要时它就是个能用的上下文，而不是一个碰一下就炸的雷。
/// </summary>
public class TestContext : IVivContext
{
    private VivContextContent? _snapshot;

    /// <summary>当前快照（写入走接口方法 <see cref="SetSnapshot"/>）</summary>
    public VivContextContent? Snapshot => _snapshot;

    /// <summary>被清了几次 —— 验「作用域结束后确实清理了上下文」</summary>
    public int ClearCalls { get; private set; }

    /// <summary>被设置过几次快照 —— 验「水合真的发生了」</summary>
    public int SetSnapshotCalls { get; private set; }

    /// <summary>打开后任何人读 AppId/SubjectId/UserId/TraceId 都会抛 —— 用来断言「这段代码没偷读上下文」</summary>
    public bool ThrowOnMemberAccess { get; set; }

    public long AppId => Read(c => c.AppId);

    public long SubjectId => Read(c => c.SubjectId);

    public long UserId => Read(c => c.UserId);

    public string TraceId => ThrowOnMemberAccess
        ? throw new InvalidOperationException("被测代码不该读上下文（TestContext.ThrowOnMemberAccess）")
        : _snapshot?.TraceId ?? "";

    public void SetSnapshot(VivContextContent model)
    {
        SetSnapshotCalls++;
        _snapshot = model;
    }

    public void Clear()
    {
        ClearCalls++;
        _snapshot = null;
    }

    public VivContextContent? GetRawSnapshot() => _snapshot;

    private long Read(Func<VivContextContent, long> selector)
    {
        if (ThrowOnMemberAccess)
            throw new InvalidOperationException("被测代码不该读上下文（TestContext.ThrowOnMemberAccess）");

        return _snapshot is null ? 0 : selector(_snapshot);
    }
}
