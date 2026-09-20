using Viv.Contracts.Attributes;
using Viv.Contracts.Events;
using Viv.Contracts.Exceptions;
using Viv.Fakes;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Nana.Options;

namespace Viv.Nana.Tests;

[VivUnitOfWork]
public sealed class UowPublishingConsumer : VivConsumer<TestApexEvent>
{
    private readonly SubscribeResult _result;

    public UowPublishingConsumer(VivConsumerDependency dependency, SubscribeResult result) : base(dependency)
        => _result = result;

    public override async Task<SubscribeResult> ReceiveMessageAsync(
        NanaEnvelope<TestApexEvent> envelope, CancellationToken cancellationToken = default)
    {
        await _localEventBus.PublishAsync(new FlushTestEvent { Payload = "data" });
        return _result;
    }
}

[VivUnitOfWork]
public sealed class UowPublishingLocalConsumer : VivLocalConsumer<LocalTestEvent>
{
    private readonly SubscribeResult _result;

    public UowPublishingLocalConsumer(VivLocalConsumerDependency dependency, SubscribeResult result) : base(dependency)
        => _result = result;

    public override async Task<SubscribeResult> ReceiveMessageAsync(
        NanaLocalEnvelope<LocalTestEvent> envelope, CancellationToken cancellationToken = default)
    {
        await _localEventBus.PublishAsync(new FlushTestEvent { Payload = "data" });
        return _result;
    }
}

/// <summary>
/// 消费者 HandleAsync 按 <c>[VivUnitOfWork]</c> 显式开合事务，且与本地事件 Flush/Discard 顺序正确。
/// </summary>
public class ConsumerUnitOfWorkTests
{
    private static NanaEnvelope<TestApexEvent> Envelope()
        => new() { MessageId = 42, Content = new TestApexEvent { Payload = "data" } };

    private static NanaLocalEnvelope<LocalTestEvent> LocalEnvelope()
        => new() { MessageId = 42, Content = new LocalTestEvent { Payload = "data" } };

    private static VivConsumerDependency Dep(
        RecordingLocalEventBus bus,
        RecordingUnitOfWork? uow = null)
        => new(
            new RecordingLogger(),
            new TestContext(),
            new RecordingEventPublisher(),
            XUnitTestMagic.CreateOptions(new NanaOptions()),
            bus,
            unitOfWork: uow);

    private static VivLocalConsumerDependency LocalDep(
        RecordingLocalEventBus bus,
        RecordingUnitOfWork? uow = null)
        => new(new RecordingLogger(), new TestContext(), new RecordingLocalEventPublisher(), bus, uow);

    [Fact]
    public void 标了特性却没有工作单元_构造即失败()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new UowPublishingConsumer(Dep(new RecordingLocalEventBus()), SubscribeResult.Success()));

        Assert.Contains("IVivUnitOfWork", ex.Message);
    }

    [Fact]
    public async Task 成功_先提交再Flush()
    {
        var bus = new RecordingLocalEventBus();
        var uow = new RecordingUnitOfWork();
        var consumer = new UowPublishingConsumer(Dep(bus, uow), SubscribeResult.Success());

        // 两个替身各记各的，光比对各自的序列证明不了先后。
        // 在提交那一刻回看总线，此刻还没 Flush 才算真的「提交先于分发」。
        uow.OnCommit = () => Assert.Empty(bus.Calls);

        await consumer.HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal("begin|commit", uow.Trace());
        Assert.Equal(new[] { "flush" }, bus.Calls);
        Assert.Equal(CancellationToken.None, Assert.Single(uow.BeginTokens));
        Assert.Equal(CancellationToken.None, Assert.Single(uow.CommitTokens));
    }

    [Fact]
    public async Task 成功_令牌只给Begin_提交不吃调用方令牌()
    {
        var bus = new RecordingLocalEventBus();
        var uow = new RecordingUnitOfWork();
        using var cts = new CancellationTokenSource();
        var consumer = new UowPublishingConsumer(Dep(bus, uow), SubscribeResult.Success());

        await consumer.HandleAsync(Envelope(), cts.Token);

        Assert.Equal(cts.Token, Assert.Single(uow.BeginTokens));
        Assert.Equal(CancellationToken.None, Assert.Single(uow.CommitTokens));
    }

    [Fact]
    public async Task 粘性提交被拒_消费不算成功_整队Discard()
    {
        // 真工作在 rollback-only 时是「先回滚、再抛 VivUnitOfWorkException」，
        // 异常从 ExecuteAsync 冒过 HandleAsync 的 finally —— 那里必须按「没成功」处理，
        // 否则成功信封会把一个已经回滚掉的事务对应的本地事件发出去。
        var bus = new RecordingLocalEventBus();
        var uow = new RecordingUnitOfWork
        {
            CommitException = new VivUnitOfWorkException("嵌套事务已标记回滚，最外层提交被拒绝。数据库已回滚。"),
        };

        await Assert.ThrowsAsync<VivUnitOfWorkException>(
            () => new UowPublishingConsumer(Dep(bus, uow), SubscribeResult.Success())
                .HandleAsync(Envelope(), CancellationToken.None));

        Assert.Equal("begin|commit", uow.Trace());
        Assert.Equal(0, bus.FlushCalls);
        Assert.Equal(1, bus.DiscardCalls);
    }

    [Fact]
    public async Task 重投_回滚Discard且VivRequeueException不被Flush吞掉()
    {
        var bus = new RecordingLocalEventBus { FlushException = new InvalidOperationException("handler炸了") };
        var uow = new RecordingUnitOfWork();
        var consumer = new UowPublishingConsumer(Dep(bus, uow), SubscribeResult.Failed(true, "重投"));

        var ex = await Assert.ThrowsAsync<VivRequeueException>(
            () => consumer.HandleAsync(Envelope(), CancellationToken.None));

        Assert.Contains("重投", ex.Message);
        Assert.Equal("begin|rollback", uow.Trace());
        Assert.Equal(0, bus.FlushCalls);
        Assert.Equal(1, bus.DiscardCalls);
    }

    [Fact]
    public async Task 失败不回队_回滚并Discard()
    {
        var bus = new RecordingLocalEventBus();
        var uow = new RecordingUnitOfWork();

        await new UowPublishingConsumer(Dep(bus, uow), SubscribeResult.Failed(false, "丢弃"))
            .HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal("begin|rollback", uow.Trace());
        Assert.Equal(0, bus.FlushCalls);
        Assert.Equal(1, bus.DiscardCalls);
    }

    [Fact]
    public async Task 本地队列_成功提交后Flush()
    {
        var bus = new RecordingLocalEventBus();
        var uow = new RecordingUnitOfWork();
        uow.OnCommit = () => Assert.Empty(bus.Calls);

        await new UowPublishingLocalConsumer(LocalDep(bus, uow), SubscribeResult.Success())
            .HandleAsync(LocalEnvelope(), CancellationToken.None);

        Assert.Equal("begin|commit", uow.Trace());
        Assert.Equal(new[] { "flush" }, bus.Calls);
    }

    [Fact]
    public async Task 本地队列_重投_回滚Discard且异常仍是VivRequeueException()
    {
        var bus = new RecordingLocalEventBus { FlushException = new InvalidOperationException("handler炸了") };
        var uow = new RecordingUnitOfWork();

        var ex = await Assert.ThrowsAsync<VivRequeueException>(
            () => new UowPublishingLocalConsumer(LocalDep(bus, uow), SubscribeResult.Failed(true, "重投"))
                .HandleAsync(LocalEnvelope(), CancellationToken.None));

        Assert.Contains("重投", ex.Message);
        Assert.Equal("begin|rollback", uow.Trace());
        Assert.Equal(0, bus.FlushCalls);
        Assert.Equal(1, bus.DiscardCalls);
    }
}
