using Viv.Engine.UnitOfWork;
using Viv.Log;

namespace Viv.Engine.Tests;

/// <summary>
/// 工作单元测试的公共桩。
/// </summary>
internal sealed class RecordingLogger : ILoggerContract
{
    public List<string> Infos { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    public void Info(string message, params object[] args) => Infos.Add(Render(message, args));
    public void Error(string message, Exception ex, params object[] args) => Errors.Add(Render(message, args));
    public void Error(string message, params object[] args) => Errors.Add(Render(message, args));
    public void Debug(string message, params object[] args) { }
    public void Warning(string message, params object[] args) => Warnings.Add(Render(message, args));
    public void Fatal(string message, params object[] args) => Errors.Add(Render(message, args));
    public void Fatal(string message, Exception ex, params object[] args) => Errors.Add(Render(message, args));

    /// <summary>
    /// 把 <c>params object[]</c> 填进模板再记下来 —— 只记模板的话，
    /// 「到底哪个方法没被覆盖」这类关键信息（都在参数里）就丢了，
    /// 断言只能匹配到半句话。
    /// </summary>
    private static string Render(string message, object[] args)
        => args.Length == 0 ? message : string.Format(message, args);
}

/// <summary>
/// 事务内核桩 —— 只记调用序列，不碰数据库。
///
/// 这个桩只有 3 个方法，正是 <see cref="ITransactionKernel"/> 存在的理由：
/// 直连 <c>IMomoDbContext</c> 的话这里要桩 55 个成员。
/// </summary>
internal sealed class KernelStub : ITransactionKernel
{
    public List<string> Calls { get; } = [];

    /// <summary>开启事务的返回值。底层实测从不返回 false，保留只为验防御性分支。</summary>
    public bool BeginResult { get; set; } = true;

    /// <summary>回滚时抛出的异常 —— 验「回滚失败只记日志不抛」</summary>
    public Exception? RollbackException { get; set; }

    /// <summary>提交时抛出的异常 —— 验「提交失败必须响亮上抛」</summary>
    public Exception? CommitException { get; set; }

    /// <summary>提交那一刻的回调 —— ★ 用于验证「提交发生在业务方法真正结束之后」</summary>
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
