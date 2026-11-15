using System.Diagnostics;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>
/// 唤醒信号本身：合并语义、超时兜底、取消不被吞。
///
/// 这几条比看起来重要 —— 它们全是「错了也不报错，只是慢」的类型：
/// 信号不合并会让投递器空转，超时被吞会让停机卡住，取消被吞会让进程退不掉。
/// </summary>
public class OutboxSignalTests
{
    /// <summary>探针等待时长：够判断「有没有被叫醒」，又不至于让用例变慢</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(80);

    [Fact]
    public async Task 有信号_立即返回true()
    {
        var signal = new OutboxSignal();
        signal.Notify();

        // 兜底间隔给到 30 秒：能立刻返回就只可能是信号
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 无信号_等到超时返回false()
    {
        var signal = new OutboxSignal();
        var started = Stopwatch.GetTimestamp();

        var woke = await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None);

        Assert.False(woke);
        // 没到点就返回会让兜底轮询变成忙等 —— 这一条钉的就是「真的等了」
        Assert.True(Stopwatch.GetElapsedTime(started) >= ProbeTimeout);
    }

    [Fact]
    public async Task 连续通知_合并成一次唤醒()
    {
        var signal = new OutboxSignal();

        // 突发入队：投递器一轮就把表排空，每条一次唤醒等于让它空转 99 次
        for (var i = 0; i < 100; i++)
        {
            signal.Notify();
        }

        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
        Assert.False(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 停机取消_原样抛出而不是当成超时()
    {
        var signal = new OutboxSignal();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 吞掉取消会让投递器的循环在停机后再等满一个轮询间隔，滚动发布被拖慢
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => signal.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System, cts.Token));
    }

    [Fact]
    public async Task 永不来的信号_就是个纯粹的等待()
    {
        // Never 给「没有快路径」的场景兜底（单测直建投递器、将来可能出现的只轮询部署），
        // 它的语义必须是「等满超时」，不能是「立刻返回」—— 后者会变成忙等
        Assert.False(await OutboxSignal.Never.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }
}

/// <summary>
/// 提交观察者：把「这个作用域入过队」翻译成「提交后叫醒一次投递器」。
/// </summary>
public class OutboxCommitObserverTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(80);

    [Fact]
    public async Task 没入队就提交_不唤醒()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);

        // 这个作用域里什么都没进发件箱（比如只读请求），叫醒投递器只会换来一轮空认领
        observer.OnCommitted();

        Assert.False(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 一次入队_只消费一次标记()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);
        observer.MarkPending();

        observer.OnCommitted();
        // 嵌套事务收尾可能重复回调，第二次不该再叫一次
        observer.OnCommitted();

        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
        Assert.False(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }

    [Fact]
    public async Task 同一作用域再入队_下一次提交还会唤醒()
    {
        var signal = new OutboxSignal();
        var observer = new OutboxCommitObserver(signal);

        observer.MarkPending();
        observer.OnCommitted();
        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));

        observer.MarkPending();
        observer.OnCommitted();

        // 一个作用域跑两笔事务（比如循环里逐条入队）时，第二笔也要叫醒 —— 标记是 0/1 而不是一次性闸门
        Assert.True(await signal.WaitAsync(ProbeTimeout, TimeProvider.System, CancellationToken.None));
    }
}
