using Microsoft.Extensions.DependencyInjection;
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
        var pool = new KinnaConnectionPool();
        pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
        pool.Backdate("c1", TimeSpan.FromMinutes(2));

        var sender = new RecordingKinnaClientSender();
        var presence = new StubKinnaPresenceSink<TestHub>();
        var sweeper = Create(pool, sender, presence, entryTtlSeconds: 30);

        await sweeper.SweepOnceAsync();

        Assert.Equal((1L, 2L), Assert.Single(presence.OfflineUsers));
        var call = Assert.Single(sender.Calls);
        Assert.Equal("c1", call.TargetId);
        Assert.Equal(KinnaClientMethods.ForceDisconnect, call.Method);
        Assert.Empty(pool.GetConnectionIds(1, 2));
    }

    [Fact]
    public async Task SweepOnce_扣在线态失败_仍软踢且不把异常抛出去()
    {
        var pool = new KinnaConnectionPool();
        pool.Add("c1", appId: 3, subjectId: 1, userId: 2);
        pool.Backdate("c1", TimeSpan.FromMinutes(2));

        var sender = new RecordingKinnaClientSender();
        var logger = new RecordingLogger();
        var sweeper = Create(pool, sender, new ThrowingPresence(), entryTtlSeconds: 30, logger: logger);

        await sweeper.SweepOnceAsync();

        Assert.Equal("c1", Assert.Single(sender.Calls).TargetId);
        Assert.Contains(logger.Errors, message => message.Contains("扣在线态失败"));
    }

    private static KinnaConnectionLeaseSweeper<TestHub> Create(
        KinnaConnectionPool pool,
        RecordingKinnaClientSender sender,
        IKinnaPresenceSink<TestHub> presence,
        int entryTtlSeconds,
        RecordingLogger? logger = null)
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
            logger ?? new RecordingLogger());
    }

    private sealed class ThrowingPresence : IKinnaPresenceSink<TestHub>
    {
        public Task OnlineAsync(long subjectId, long userId) => Task.CompletedTask;

        public Task OfflineAsync(long subjectId, long userId) => throw new InvalidOperationException("redis down");

        public Task RenewAsync(long subjectId, long userId) => Task.CompletedTask;
    }
}
