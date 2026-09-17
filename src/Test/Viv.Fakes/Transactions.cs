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

    /// <summary>用 "|" 拼出来的调用序列，便于整串断言</summary>
    public string Trace() => string.Join("|", Calls);

    public int Count(string call) => Calls.Count(c => c == call);

    public Task<bool> BeginAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("begin");
        return Task.FromResult(BeginResult);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("commit");
        if (OnCommit != null) await OnCommit().ConfigureAwait(false);
        if (CommitException != null) throw CommitException;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("rollback");
        return RollbackException is null ? Task.CompletedTask : Task.FromException(RollbackException);
    }
}
