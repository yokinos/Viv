using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Viv.Fakes;
using Viv.Kinna.Connections;
using Viv.Kinna.Contracts;
using Viv.Kinna.Options;
using Viv.Kinna.Ports;

namespace Viv.Kinna.Tests;

public class KinnaConnectionLeaseSweeperTests
{
    [Fact]
    public async Task SweepOnce_超租约连接_扣在线态并软踢()
    {
        var time = new FakeTimeProvider();
        var pool = new KinnaConnectionPool(timeProvider: time);
        pool.Add("c1", appId: 3, subjectId: 1, userId: 2);

        // 推过租约，而不是把时间戳硬拨到过去
        time.Advance(TimeSpan.FromMinutes(2));

        var sender = new RecordingKinnaClientSender();
        var presence = new StubKinnaPresenceSink<TestHub>();
        var sweeper = Create(pool, sender, presence, entryTtlSeconds: 30, time: time);

        await sweeper.SweepOnceAsync();

        Assert.Equal((1L, 2L), Assert.Single(presence.OfflineUsers));
        var call = Assert.Single(sender.Calls);
        Assert.Equal("c1", call.TargetId);
        Assert.Equal(KinnaClientMethods.ForceDisconnect, call.Method);
        Assert.Empty(pool.GetConnectionIds(1, 2));
    }

    [Fact]
    public async Task SweepOnce_租约内_一条都不摘()
    {
        var time = new FakeTimeProvider();
        var pool = new KinnaConnectionPool(timeProvider: time);
        pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
        time.Advance(TimeSpan.FromSeconds(29));

        var sender = new RecordingKinnaClientSender();
        var presence = new StubKinnaPresenceSink<TestHub>();
        var sweeper = Create(pool, sender, presence, entryTtlSeconds: 30, time: time);

        await sweeper.SweepOnceAsync();

        Assert.Empty(presence.OfflineUsers);
        Assert.Empty(sender.Calls);
        Assert.Single(pool.GetConnectionIds(1, 2));
    }

    [Fact]
    public async Task SweepOnce_扣在线态失败_仍软踢且不把异常抛出去()
    {
        var time = new FakeTimeProvider();
        var pool = new KinnaConnectionPool(timeProvider: time);
        pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
        time.Advance(TimeSpan.FromMinutes(2));

        var sender = new RecordingKinnaClientSender();
        var logger = new RecordingLogger();
        var sweeper = Create(pool, sender, new ThrowingPresence(), entryTtlSeconds: 30, time: time, logger: logger);

        await sweeper.SweepOnceAsync();

        Assert.Equal("c1", Assert.Single(sender.Calls).TargetId);
        Assert.Contains(logger.Errors, message => message.Contains("扣在线态失败"));
    }

    [Fact]
    public async Task 后台循环_按间隔重复清扫_由时钟驱动而不是真等()
    {
        var time = new FakeTimeProvider();
        var pool = new KinnaConnectionPool(timeProvider: time);
        var sender = new RecordingKinnaClientSender();
        var clock = new KinnaSweepClock<TestHub>(time);
        var sweeper = Create(pool, sender, new StubKinnaPresenceSink<TestHub>(),
            entryTtlSeconds: 30, time: time, clock: clock);

        pool.Add("c1", appId: 3, subjectId: 1, userId: 2);

        await sweeper.StartAsync(CancellationToken.None);
        try
        {
            // 先扫一轮（启动即扫）：连接刚登记，什么都不该摘
            await WaitUntilAsync(() => clock.LastSweepAt != null, "首轮清扫");
            Assert.Empty(sender.Calls);

            // 一圈圈往前拨：绕过「循环刚好还没走到 Task.Delay」的竞态，
            // 真实等待用假时钟替掉，整条循环不必真等 30 秒
            await WaitUntilAsync(
                () =>
                {
                    time.Advance(TimeSpan.FromSeconds(1));
                    return sender.Calls.Count > 0;
                },
                "第二轮清扫摘掉超租约连接");
        }
        finally
        {
            await sweeper.StopAsync(CancellationToken.None);
        }

        Assert.Equal("c1", sender.Calls[0].TargetId);
        Assert.Empty(pool.GetConnectionIds(1, 2));
    }

    private static KinnaConnectionLeaseSweeper<TestHub> Create(
        KinnaConnectionPool pool,
        RecordingKinnaClientSender sender,
        IKinnaPresenceSink<TestHub> presence,
        int entryTtlSeconds,
        TimeProvider time,
        RecordingLogger? logger = null,
        KinnaSweepClock<TestHub>? clock = null)
    {
        var session = new StubKinnaSession<TestHub>(
            pool,
            sender,
            new KinnaKeys("kinna"),
            new KinnaOptions { EntryTtlSeconds = entryTtlSeconds, SweepIntervalSeconds = 30 });

        var services = new ServiceCollection();
        services.AddSingleton(presence);
        var provider = services.BuildServiceProvider();

        return new KinnaConnectionLeaseSweeper<TestHub>(
            session,
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? new RecordingLogger(),
            clock,
            timeProvider: time);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 500; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"等待超时: {what}");
    }

    private sealed class ThrowingPresence : IKinnaPresenceSink<TestHub>
    {
        public Task OnlineAsync(long subjectId, long userId) => Task.CompletedTask;

        public Task OfflineAsync(long subjectId, long userId) => throw new InvalidOperationException("redis down");

        public Task RenewAsync(long subjectId, long userId) => Task.CompletedTask;
    }
}
