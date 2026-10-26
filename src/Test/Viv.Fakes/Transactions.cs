using Viv.Contracts.Interface;
using Viv.Engine.UnitOfWork;

namespace Viv.Fakes;

/// <summary>
/// 事务内核桩 —— 只记调用序列，不碰数据库。
///
/// 这个桩只有 1 个方法，正是 <see cref="ITransactionKernel"/> 存在的理由：
/// 直连 <c>IMomoDbContext</c> 的话这里要桩五十多个成员。
///
/// 只能声明成 internal：<see cref="ITransactionKernel"/> 本身就是 internal 的，
/// public 类实现 internal 接口是 CS0061。故本程序集对 <c>Viv.Engine.Tests</c> 开了 IVT。
/// </summary>
internal class KernelStub : ITransactionKernel
{
    public List<string> Calls { get; } = [];

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

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> work,
        Func<T, bool>? commitWhen,
        CancellationToken cancellationToken)
    {
        Calls.Add("begin");
        BeginTokens.Add(cancellationToken);

        T result;
        try
        {
            result = await work(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 回滚失败不能盖住原始异常（与 Momo 实现同一取舍）
            try
            {
                await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 只记日志的位置在 Momo 那边，桩这里直接吞
            }

            throw;
        }

        if (commitWhen is not null && !commitWhen(result))
        {
            await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        await CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        Calls.Add("commit");
        CommitTokens.Add(cancellationToken);
        if (OnCommit != null) await OnCommit().ConfigureAwait(false);
        if (CommitException != null) throw CommitException;
    }

    private Task RollbackAsync(CancellationToken cancellationToken)
    {
        Calls.Add("rollback");
        RollbackTokens.Add(cancellationToken);
        return RollbackException is null ? Task.CompletedTask : Task.FromException(RollbackException);
    }
}

/// <summary>
/// 公开的工作单元记录替身 —— 给跨程序集测试（如 Nana 消费者）用，不必碰 internal 的内核桩。
/// 记录序列与真实实现一致：begin → commit / begin → rollback。
/// </summary>
public sealed class RecordingUnitOfWork : IVivUnitOfWork
{
    public List<string> Calls { get; } = [];

    public List<CancellationToken> BeginTokens { get; } = [];

    public List<CancellationToken> CommitTokens { get; } = [];

    public List<CancellationToken> RollbackTokens { get; } = [];

    /// <summary>提交那一刻的回调 —— 与 <see cref="KernelStub.OnCommit"/> 同款，验时序用</summary>
    public Action? OnCommit { get; set; }

    /// <summary>提交时抛出的异常 —— 验「提交失败必须上抛」</summary>
    public Exception? CommitException { get; set; }

    /// <summary>回滚时抛出的异常</summary>
    public Exception? RollbackException { get; set; }

    public string Trace() => string.Join("|", Calls);

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> work,
        Func<T, bool>? commitWhen = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("begin");
        BeginTokens.Add(cancellationToken);

        T result;
        try
        {
            result = await work(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackAsync().ConfigureAwait(false);
            throw;
        }

        if (commitWhen is not null && !commitWhen(result))
        {
            await RollbackAsync().ConfigureAwait(false);
            return result;
        }

        Calls.Add("commit");
        CommitTokens.Add(CancellationToken.None);
        OnCommit?.Invoke();
        if (CommitException != null) throw CommitException;

        return result;
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return ExecuteAsync(async ct =>
        {
            await work(ct).ConfigureAwait(false);
            return true;
        }, commitWhen: null, cancellationToken);
    }

    private Task RollbackAsync()
    {
        Calls.Add("rollback");
        RollbackTokens.Add(CancellationToken.None);
        return RollbackException is null ? Task.CompletedTask : Task.FromException(RollbackException);
    }
}
