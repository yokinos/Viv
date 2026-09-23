using System.Reflection;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Nana.Tests
{
    /// <summary>
    /// 本地队列（NanaLocalEvent / NanaLocalEnvelope）的类型约束与信封语义。
    /// </summary>
    public class NanaLocalEventTests
    {
        [Fact]
        public void NanaLocalEvent是空标记基类_且与NanaEvent互不继承()
        {
            const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            // 纯限制：抽象 + 自身零成员
            Assert.True(typeof(NanaLocalEvent).IsAbstract);
            Assert.Equal(typeof(object), typeof(NanaLocalEvent).BaseType);
            Assert.Empty(typeof(NanaLocalEvent).GetFields(declared));
            Assert.Empty(typeof(NanaLocalEvent).GetProperties(declared));
            Assert.Empty(typeof(NanaLocalEvent).GetMethods(declared));

            // ★ 本次最关键的一条回归防护 ★
            // 一旦 NanaLocalEvent 继承 NanaEvent，VivWolverineConfigurationExtensions 里那段
            // foreach (var eventType in TypeScanMagic.ScanTypes<NanaEvent>()) 就会给每个本地事件
            // 注册 ToRabbitExchange 路由 —— 之后一次 PublishAsync 变成「RabbitMQ + 本地队列」双发，
            // 而且编译期毫无提示。名字带 Nana 很容易让人顺手写成 : NanaEvent，所以这里钉死。
            Assert.False(typeof(NanaEvent).IsAssignableFrom(typeof(NanaLocalEvent)));

            // 反向同理：NanaEvent 也不该反过来继承本地事件
            Assert.False(typeof(NanaLocalEvent).IsAssignableFrom(typeof(NanaEvent)));
        }

        [Fact]
        public void NanaLocalEnvelope_默认MessageId非零()
        {
            var e = new NanaLocalEnvelope<LocalTestEvent>();
            Assert.NotEqual(0L, e.MessageId);
        }

        [Fact]
        public void NanaLocalEnvelope_CreatedAt接近当前时间()
        {
            var before = DateTimeOffset.UtcNow.AddSeconds(-10);
            var e = new NanaLocalEnvelope<LocalTestEvent>();
            var after = DateTimeOffset.UtcNow.AddSeconds(10);

            Assert.InRange(e.CreatedAt, before, after);
        }

        [Fact]
        public void NanaLocalEnvelope_可承载内容与上下文()
        {
            var e = new NanaLocalEnvelope<LocalTestEvent>
            {
                Content = new LocalTestEvent { Payload = "x" },
                Context = new VivContextContent { AppId = 1, SubjectId = 3, UserId = 2, HolderId = "h-1" }
            };

            Assert.Equal("x", e.Content?.Payload);
            Assert.Equal(1, e.Context?.AppId);
            Assert.Equal(3, e.Context?.SubjectId);
            Assert.Equal(2, e.Context?.UserId);
            Assert.Equal("h-1", e.Context?.HolderId);
        }

        [Fact]
        public void 本地消费者依赖包_不带分布式锁()
        {
            // 本地队列在本进程、点对点、无 fanout，不存在多实例抢同一把锁的场景。
            // 依赖包一旦长出 IDistributedLock 字段，就说明有人把 VivConsumer 那套锁逻辑抄过来了。
            var fields = typeof(VivLocalConsumerDependency)
                .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => f.FieldType).ToList();

            Assert.Contains(typeof(IVivContext), fields);
            Assert.Contains(typeof(IVivLocalEventPublisher), fields);
            Assert.DoesNotContain(typeof(IDistributedLock), fields);

            // 同理也不该捆着跨进程的发布器（那就是 VivConsumerDependency 了）
            Assert.DoesNotContain(typeof(IVivEventPublisher), fields);
        }
    }
}
