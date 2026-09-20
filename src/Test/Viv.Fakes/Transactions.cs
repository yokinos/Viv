using Viv.Contracts.Interface;
using Viv.Engine.UnitOfWork;

namespace Viv.Fakes;

/// <summary>
/// 事务内核桩 —— 只记调用序列，不碰数据库。
///
/// 这个桩只有 3 个方法，正是 <see cref="ITransactionKernel"/> 存在的理由：
/// 直连 <c>IMomoDbContext</c> 的话这里要桩 55 个成员。
///
/// 只能声明成 internal：<see cref="ITransactionKernel"/> 本身就是 internal 的，
/// public 类实现 internal 接口是 CS0061。故本程序集对 <c>Viv.Engine.Tests</c> 开了 IVT。
/// </summary>
internal class KernelStub : ITransactionKernel
{
    public List<string> Calls { get; } = [];

    /// <summary>开启事务的返回值。底层实测从不返回 false，保留只为验防御性分支。</summary>
    public bool BeginResult { get; set; } = true;

    /// <summary>回滚时抛出的异常 —— 验「回滚失败只记日志不抛」</summary>
    public Exception? RollbackException { get; set; }

    /// <summary>提交时抛出的异常 —— 验「提交失败必须响亮上抛」</summary>
    public Exception? CommitException { get; set; }

    /// <summary>提交那一刻的回调 —— 用于验证「提交发生在业务方法真正结束之后」</summary>
    public Func<Task>? OnCommit { get; set; }

    public List<CancellationToken> BeginTokens { get; } = [];

    public List<CancellationToken> CommitTokens { get; } = [];

    public List<CancellationToken> RollbackTokens { get; } = [];

    /// <summary>用 "|" 拼出来的调用序列，便于整串断言</summary>
    public string Trace() => string.Join("|", Calls);

    public int Count(string call) => Calls.Count(c => c == call);

    public Task<bool> BeginAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("begin");
        BeginTokens.Add(cancellationToken);
        return Task.FromResult(BeginResult);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("commit");
        CommitTokens.Add(cancellationToken);
        if (OnCommit != null) await OnCommit().ConfigureAwait(false);
        if (CommitException != null) throw CommitException;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("rollback");
        RollbackTokens.Add(cancellationToken);
        return RollbackException is null ? Task.CompletedTask : Task.FromException(RollbackException);
    }
}

/// <summary>
/// 公开的工作单元记录替身 —— 给跨程序集测试（如 Nana 消费者）用，不必碰 internal 的内核桩。
/// </summary>
public sealed class RecordingUnitOfWork : IVivUnitOfWork
{
    public List<string> Calls { get; } = [];

    public List<CancellationToken> BeginTokens { get; } = [];

    public List<CancellationToken> CommitTokens { get; } = [];

    public List<CancellationToken> RollbackTokens { get; } = [];

    public string Trace() => string.Join("|", Calls);

    public Task<IVivTransaction> BeginAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("begin");
        BeginTokens.Add(cancellationToken);
        return Task.FromResult<IVivTransaction>(new Handle(this));
    }

    private sealed class Handle : IVivTransaction
    {
        private readonly RecordingUnitOfWork _owner;
        private bool _completed;

        public Handle(RecordingUnitOfWork owner) => _owner = owner;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_completed) return Task.CompletedTask;
            _completed = true;
            _owner.Calls.Add("commit");
            _owner.CommitTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_completed) return Task.CompletedTask;
            _completed = true;
            _owner.Calls.Add("rollback");
            _owner.RollbackTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public ValueTask DisposeAsync()
            => new(RollbackAsync(CancellationToken.None));
    }
}
