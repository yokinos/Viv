using System.Text.Json;
using Viv.Nana;
using Viv.Outbox.Core;

namespace Viv.Outbox.Tests;

/// <summary>
/// 契约与序列化的防回归。这里每一条钉的都是「改一行就静默出问题」的决定。
/// </summary>
public class OutboxContractTests
{
    [Fact]
    public void OutboxMessage既不实现IEntity也不实现ITenant()
    {
        var interfaces = typeof(OutboxMessage).GetInterfaces().Select(x => x.Name).ToList();

        // ★ 为什么不碰 EF：EF 一接管，表名/列名就由命名约定生成（outbox_message vs OutboxMessage），
        //   而手写 SQL 用的是不带引号的 PascalCase —— 两套命名一对不上就是运行期才炸。
        //   另外 ITenant 会带来全局查询过滤器：投递器跑在后台作用域里，
        //   拿它去读「没有租户」的行会被静默过滤成一条都读不到。
        Assert.DoesNotContain("IEntity", interfaces);
        Assert.DoesNotContain("ITenant", interfaces);
    }

    [Fact]
    public void OutboxMessage不继承任何实体基类()
    {
        // 不继承 = 扫描 EF 实体时连候选都不是，从根上够不到它
        Assert.Equal(typeof(object), typeof(OutboxMessage).BaseType);
    }

    [Fact]
    public void OutboxMessage没标EF表名特性()
    {
        // 标了 TableAttribute 说明有人想把它拉进 EF —— 那这条防线就破了
        foreach (var attribute in typeof(OutboxMessage).GetCustomAttributes(false))
        {
            Assert.DoesNotContain("TableAttribute", attribute.GetType().Name);
            Assert.DoesNotContain("KeylessAttribute", attribute.GetType().Name);
        }
    }

    [Fact]
    public void 状态数值即库里的值()
    {
        // 这些数字直接落在 TINYINT / SMALLINT 列里，改一次就等于把所有历史行解读错
        Assert.Equal(0, (int)OutboxStatus.Pending);
        Assert.Equal(1, (int)OutboxStatus.Processing);
        Assert.Equal(2, (int)OutboxStatus.Sent);
        Assert.Equal(3, (int)OutboxStatus.Failed);
    }

    [Fact]
    public void 序列化选项是camelCase且大小写不敏感()
    {
        var json = JsonSerializer.Serialize(new OutboxTestEvent { Payload = "x", Number = 1 }, OutboxJson.Options);

        Assert.Contains("\"payload\"", json);
        Assert.Contains("\"number\"", json);
    }

    [Fact]
    public void 序列化选项_大小写不敏感_兼容手改过的payload()
    {
        // 排查时手工改过库里的 JSON（或者别的工具写的大小写），不能因此解析不出来
        var back = JsonSerializer.Deserialize<OutboxTestEvent>("{\"PAYLOAD\":\"y\",\"NUMBER\":5}", OutboxJson.Options);

        Assert.NotNull(back);
        Assert.Equal("y", back.Payload);
        Assert.Equal(5, back.Number);
    }

    [Fact]
    public void 信封序列化往返_保留MessageId与上下文与重投计数()
    {
        var envelope = new NanaEnvelope<OutboxTestEvent>
        {
            Content = new OutboxTestEvent { Payload = "p", Number = 7 },
            MessageId = 123456789,
            ReDeliverCount = 3,
            DelaySecond = 12.5,
            Context = new Viv.Contracts.Models.VivContextContent
            {
                AppId = 1,
                SubjectId = 42,
                UserId = 7,
                TraceId = "t-1",
                HolderId = "h-1",
            },
        };

        var json = JsonSerializer.Serialize(envelope, OutboxJson.Options);
        var back = JsonSerializer.Deserialize<NanaEnvelope<OutboxTestEvent>>(json, OutboxJson.Options);

        Assert.NotNull(back);
        Assert.Equal(123456789, back.MessageId);
        Assert.Equal(3, back.ReDeliverCount);
        Assert.Equal(12.5, back.DelaySecond);
        Assert.Equal("p", back.Content!.Payload);
        Assert.Equal(7, back.Content.Number);

        Assert.NotNull(back.Context);
        Assert.Equal(1, back.Context.AppId);
        Assert.Equal(42, back.Context.SubjectId);
        Assert.Equal(7, back.Context.UserId);
        Assert.Equal("t-1", back.Context.TraceId);
        Assert.Equal("h-1", back.Context.HolderId);
    }

    [Fact]
    public void 事件类型约束_必须继承NanaEvent()
    {
        // 发件箱投的是跨进程那族：复用现成的 {EventName}Exchange fanout 拓扑，
        // 也复用消费端 VivConsumer<T>。约束在编译期挡住别的族混进来。
        var enqueue = typeof(IVivOutbox).GetMethods().Single(x => x.Name == nameof(IVivOutbox.EnqueueAsync));
        var constraint = enqueue.GetGenericArguments().Single().BaseType;

        Assert.NotNull(constraint);
        Assert.Contains(typeof(NanaEvent), Flatten(constraint!));

        static IEnumerable<Type> Flatten(Type type)
        {
            for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            {
                yield return current;
            }
        }
    }

    [Fact]
    public void 契约只暴露一个入队方法_没有同步重载()
    {
        // 同步版本会诱使人写出「在事务里同步等落库」—— 那个场景下事务是开着的，不值得单开一个口子
        var methods = typeof(IVivOutbox).GetMethods();

        Assert.Single(methods);
        Assert.Equal(nameof(IVivOutbox.EnqueueAsync), methods[0].Name);
    }
}
