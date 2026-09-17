using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viv.Nana;
using Viv.Outbox.Core;
using Viv.Outbox.Options;

namespace Viv.Outbox.Tests;

/// <summary>
/// 后台宿主那一层：循环、睡眠、吞异常。
/// 只测一件真正要命的事 —— <b>投递轮次的异常绝不能逃出去</b>。
/// 后台服务里逃出去的异常在 .NET 6+ 会直接停掉整个宿主：MQ 抖一下整个业务进程跟着死。
/// </summary>
public class OutboxDispatcherTests
{
    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }

        return condition();
    }

    private static (OutboxDispatcher Dispatcher, ServiceProvider Provider) Build(
        StubOutboxRepository repo, StubPublisher publisher, StubLogger logger)
    {
        var services = new ServiceCollection();
        services.AddScoped<IOutboxRepository>(_ => repo);
        services.AddScoped<IVivEventPublisher>(_ => publisher);
        var provider = services.BuildServiceProvider();

        var dispatcher = new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new OutboxEnvelopeSenderFactory(),
            logger,
            // 必须全限定：本测试的命名空间嵌在 Viv.Outbox 下，裸写 Options 会被解析成 Viv.Outbox.Options
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions { PollIntervalSeconds = 1 }));

        return (dispatcher, provider);
    }

    [Fact]
    public async Task 轮次异常被吞掉_不拖垮宿主机()
    {
        var repo = new StubOutboxRepository { ClaimException = new Exception("认领炸了") };
        var logger = new StubLogger();
        var (dispatcher, provider) = Build(repo, new StubPublisher(), logger);

        try
        {
            await dispatcher.StartAsync(CancellationToken.None);

            Assert.True(
                await WaitForAsync(() => logger.Errors.Any(x => x.Contains("已吞掉")), TimeSpan.FromSeconds(10)),
                "投递轮次的异常没有被吞掉并记日志");

            // 一轮炸了之后必须还活着继续跑 —— 这才叫「吞掉」
            var callsAfterFirstError = repo.ReleaseExpiredCalls;
            Assert.True(
                await WaitForAsync(() => repo.ReleaseExpiredCalls > callsAfterFirstError, TimeSpan.FromSeconds(10)),
                "一轮异常之后调度器没有再跑下一轮");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task 正常投递_记一条本轮投递数()
    {
        var repo = new StubOutboxRepository();
        var logger = new StubLogger();

        var message = new OutboxMessage
        {
            Id = 1,
            MessageId = 10,
            EventType = typeof(OutboxTestEvent).FullName!,
            Payload = TestPayload.For(new OutboxTestEvent { Payload = "p" }, 10),
            Status = OutboxStatus.Processing,
            OccurredAt = DateTime.UtcNow,
            NextRetryAt = DateTime.UtcNow,
        };
        repo.ClaimScript.Enqueue(new List<OutboxMessage> { message });

        var (dispatcher, provider) = Build(repo, new StubPublisher(), logger);

        try
        {
            await dispatcher.StartAsync(CancellationToken.None);

            Assert.True(
                await WaitForAsync(() => repo.Sent.Count == 1, TimeSpan.FromSeconds(10)),
                "消息没有被投递出去");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }

        Assert.Contains(logger.Infos, x => x.Contains("本轮投递 1 条"));
    }

    [Fact]
    public async Task 启动_先建表再进循环()
    {
        var repo = new StubOutboxRepository();
        var logger = new StubLogger();
        var (dispatcher, provider) = Build(repo, new StubPublisher(), logger);

        try
        {
            await dispatcher.StartAsync(CancellationToken.None);

            Assert.True(
                await WaitForAsync(() => repo.EnsureTableCalls >= 1, TimeSpan.FromSeconds(10)),
                "投递器启动时没有建表");
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
