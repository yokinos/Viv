using System.Text.Json;
using Viv.Fakes;
using Viv.Nana;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>
/// 投递时的类型解析与闭合泛型派发。
/// 依赖 <c>TypeScanMagic.ScanTypes&lt;NanaEvent&gt;()</c> 能扫到本测试程序集里的 <see cref="OutboxTestEvent"/>。
/// </summary>
public class OutboxEnvelopeSenderFactoryTests
{
    private static string EventTypeName => typeof(OutboxTestEvent).FullName!;

    [Fact]
    public async Task 类型解析_闭合泛型重建出具体Content类型()
    {
        var sender = new OutboxEnvelopeSenderFactory().Resolve(EventTypeName);

        Assert.NotNull(sender);

        var publisher = new RecordingEventPublisher();
        var payload = TestPayload.For(new OutboxTestEvent { Payload = "hello", Number = 3 }, messageId: 12345);

        Assert.True(await sender!.SendAsync(publisher, payload, 12345, CancellationToken.None));

        // 必须重建出 NanaEnvelope<OutboxTestEvent> 这个闭合类型。
        // 若退化成 NanaEnvelope<NanaEvent>，消费端的 Discover 会认不出事件路由。
        var envelope = Assert.IsType<NanaEnvelope<OutboxTestEvent>>(Assert.Single(publisher.Envelopes));
        Assert.Equal("hello", envelope.Content!.Payload);
        Assert.Equal(3, envelope.Content.Number);
    }

    [Fact]
    public async Task 投递_MessageId以数据库列为准_覆盖payload里的旧值()
    {
        var sender = new OutboxEnvelopeSenderFactory().Resolve(EventTypeName);
        var publisher = new RecordingEventPublisher();

        // payload 里写一个错的 MessageId：它只是历史快照，真相在列上
        var payload = TestPayload.For(new OutboxTestEvent { Payload = "x" }, messageId: 111);

        await sender!.SendAsync(publisher, payload, 999, CancellationToken.None);

        var envelope = publisher.Last<OutboxTestEvent>();
        Assert.NotNull(envelope);

        // MessageId 是消费端 nana:{ServiceName}:{EventType}:{MessageId} 这把锁的去重键，必须回填
        Assert.Equal(999, envelope.MessageId);
    }

    [Fact]
    public async Task 投递_保留payload里的上下文与重投计数()
    {
        var sender = new OutboxEnvelopeSenderFactory().Resolve(EventTypeName);
        var publisher = new RecordingEventPublisher();

        var payload = TestPayload.For(
            new OutboxTestEvent { Payload = "x" },
            messageId: 1,
            context: new Viv.Contracts.Models.VivContextContent { SubjectId = 42, HolderId = "h-1" });

        await sender!.SendAsync(publisher, payload, 1, CancellationToken.None);

        var envelope = publisher.Last<OutboxTestEvent>()!;
        Assert.Equal(42, envelope.Context!.SubjectId);
        Assert.Equal("h-1", envelope.Context.HolderId);
    }

    [Fact]
    public async Task payload内容为null_返回false且不投递()
    {
        var sender = new OutboxEnvelopeSenderFactory().Resolve(EventTypeName);
        var publisher = new RecordingEventPublisher();

        // 只有元数据、没有 Content 的信封 —— 发出去只会让消费端炸
        var payload = JsonSerializer.Serialize(new NanaEnvelope<OutboxTestEvent>(), TestPayload.JsonOptions);

        Assert.False(await sender!.SendAsync(publisher, payload, 1, CancellationToken.None));
        Assert.Empty(publisher.Envelopes);
    }

    [Fact]
    public async Task payload是坏JSON_上抛异常由投递器去重试()
    {
        var sender = new OutboxEnvelopeSenderFactory().Resolve(EventTypeName);
        var publisher = new RecordingEventPublisher();

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await sender!.SendAsync(publisher, "{ 这不是 json", 1, CancellationToken.None));
    }

    [Fact]
    public void 未知事件类型_返回null()
    {
        Assert.Null(new OutboxEnvelopeSenderFactory().Resolve("Viv.Nowhere.NoSuchEvent"));
    }

    [Fact]
    public void 空事件类型_返回null()
    {
        var factory = new OutboxEnvelopeSenderFactory();

        Assert.Null(factory.Resolve(string.Empty));
        Assert.Null(factory.Resolve(null!));
    }

    [Fact]
    public void 把非NanaEvent类型名递进来_也返回null()
    {
        // 解析走的是「先查索引、查不到再 Type.GetType」，索引里只有 NanaEvent 子类；
        // 这里递的是个结构完全不同的类型名，必须被挡住而不是构造出派发器
        Assert.Null(new OutboxEnvelopeSenderFactory().Resolve(typeof(StubOutboxRepository).FullName!));
        Assert.Null(new OutboxEnvelopeSenderFactory().Resolve(typeof(string).FullName!));
    }

    [Fact]
    public void 解析结果被缓存_同一名字返回同一实例()
    {
        var factory = new OutboxEnvelopeSenderFactory();

        var first = factory.Resolve(EventTypeName);
        var second = factory.Resolve(EventTypeName);

        // 每条消息都反射一次 MakeGenericType 是这条路径上最贵的开销，必须只付一次
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void 解析不出来的结果也被缓存_不会每条消息都重扫一遍()
    {
        var factory = new OutboxEnvelopeSenderFactory();

        Assert.Null(factory.Resolve("Viv.Nowhere.NoSuchEvent"));
        Assert.Null(factory.Resolve("Viv.Nowhere.NoSuchEvent"));
    }
}
