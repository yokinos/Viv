using System.Reflection;
using Viv.Delusion;
using Viv.Delusion.Magic;
using Viv.Nana.Options;

namespace Viv.Nana.Tests
{
    /// <summary>
    /// NanaRegister 拓扑命名约定 —— 发布方 / 消费方 / 跨服务队列名必须一致，
    /// 这里是"一次执行一次广播"消息契约的静态锚点，任何一处漂移都会导致消息收不到。
    /// </summary>
    public class NanaRegisterTests
    {
        [Fact]
        public void GetQueueName_去Event后缀()
        {
            Assert.Equal("TestApexQueue", NanaRegister.GetQueueName(typeof(TestApexEvent)));
        }

        [Fact]
        public void GetQueueName_大小写不敏感()
        {
            Assert.Equal("LowerQueue", NanaRegister.GetQueueName(typeof(LowerEvent)));
        }

        [Fact]
        public void GetQueueName_无Event后缀原样拼接()
        {
            Assert.Equal("PlainMessageQueue", NanaRegister.GetQueueName(typeof(PlainMessage)));
        }

        [Fact]
        public void GetExchangeName_去Event后缀()
        {
            Assert.Equal("TestApexExchange", NanaRegister.GetExchangeName(typeof(TestApexEvent)));
        }

        [Fact]
        public void GetLocalQueueName_去Event后缀()
        {
            Assert.Equal("TestApexLocalQueue", NanaRegister.GetLocalQueueName(typeof(TestApexEvent)));
        }

        [Fact]
        public void GetLocalQueueName_大小写不敏感()
        {
            Assert.Equal("LowerLocalQueue", NanaRegister.GetLocalQueueName(typeof(LowerEvent)));
        }

        [Fact]
        public void GetLocalQueueName_无Event后缀原样拼接()
        {
            Assert.Equal("PlainMessageLocalQueue", NanaRegister.GetLocalQueueName(typeof(PlainMessage)));
        }

        [Fact]
        public void ExtractLocalMessageType_从VivLocalConsumer提取T()
        {
            Assert.Equal(typeof(LocalTestEvent), NanaRegister.ExtractLocalMessageType(typeof(RecordingLocalConsumer)));
        }

        [Fact]
        public void ExtractLocalMessageType_对RabbitMQ消费者返回null()
        {
            // 两条消费者线互不认领对方：VivConsumer 子类不是本地消费者，VivLocalConsumer 子类也不是 RabbitMQ 消费者
            Assert.Null(NanaRegister.ExtractLocalMessageType(typeof(RequeueConsumer)));
            Assert.Null(NanaRegister.ExtractMessageType(typeof(RecordingLocalConsumer)));
        }

        [Fact]
        public void GetConsumerQueueName_拼接服务名()
        {
            Assert.Equal(
                "TestApexQueue.viv.apex.worker",
                NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), "viv.apex.worker"));
        }

        [Fact]
        public void GetConsumerQueueName_非广播不带实例段()
        {
            // 默认拓扑的队列名必须与改造前逐字一致，否则全仓所有服务都在换队列
            Assert.Equal(
                "TestApexQueue.viv.apex.worker",
                NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), "viv.apex.worker", broadcast: false, instanceId: "link-01"));
        }

        [Fact]
        public void GetConsumerQueueName_广播追加实例段()
        {
            Assert.Equal(
                "TestApexQueue.viv.apex.worker.link-01",
                NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), "viv.apex.worker", broadcast: true, instanceId: "link-01"));
        }

        [Fact]
        public void GetConsumerQueueName_广播与默认只差最后一段()
        {
            const string service = "viv.herta.link";
            var normal = NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), service);
            var broadcast = NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), service, broadcast: true, instanceId: "pod-a");

            Assert.Equal(normal + ".pod-a", broadcast);
        }

        [Fact]
        public void GetConsumerQueueName_两个实例得到不同队列()
        {
            // 同名 = 两个副本抢一条队列，表现是随机漏推，没有异常也没有日志
            const string service = "viv.herta.link";
            var a = NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), service, broadcast: true, instanceId: "pod-a");
            var b = NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), service, broadcast: true, instanceId: "pod-b");

            Assert.NotEqual(a, b);
        }

        [Theory]
        [InlineData("my host", "my-host")]
        [InlineData("a/b:c", "a-b-c")]
        [InlineData("pod_1.2", "pod_1.2")]
        [InlineData("--x--", "x")]
        public void NormalizeInstanceId_净化非法字符(string raw, string expected)
        {
            Assert.Equal(expected, NanaRegister.NormalizeInstanceId(raw));
        }

        [Fact]
        public void NormalizeInstanceId_全非法回退instance()
        {
            Assert.Equal("instance", NanaRegister.NormalizeInstanceId("!!!"));
        }

        [Fact]
        public void NormalizeInstanceId_截断到64()
        {
            Assert.Equal(64, NanaRegister.NormalizeInstanceId(new string('a', 100)).Length);
        }

        [Fact]
        public void RecordBroadcastQueues_覆盖静态快照()
        {
            NanaRegister.RecordBroadcastQueues(new[] { "AQueue.svc.a", "BQueue.svc.a" });

            Assert.Equal(new[] { "AQueue.svc.a", "BQueue.svc.a" }, NanaRegister.BroadcastQueues);
        }

        [Fact]
        public void NanaConsumerAttribute_Topology默认共享()
        {
            // Shared 必须是 0：特性不写参数时用的就是它
            Assert.Equal(0, (int)ConsumerTopology.Shared);
            Assert.Equal(ConsumerTopology.Shared, new NanaConsumerAttribute().Topology);
        }

        [Fact]
        public void NanaConsumerAttribute_只能标类且不继承()
        {
            // Inherited = false：子类不能顺带继承父类的接线方式
            var usage = typeof(NanaConsumerAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal(AttributeTargets.Class, usage.ValidOn);
            Assert.False(usage.Inherited);
            Assert.False(usage.AllowMultiple);
        }

        [Fact]
        public void GetConsumerLockKey_含服务名事件和MessageId()
        {
            var key = NanaRegister.GetConsumerLockKey(nameof(TestApexEvent), 42);

            Assert.Equal($"nana:{NanaRegister.CurrentServiceName}:TestApexEvent:42", key);
            Assert.StartsWith("nana:", key);
        }

        [Fact]
        public void ExtractMessageType_从VivConsumer提取T()
        {
            Assert.Equal(typeof(TestApexEvent), NanaRegister.ExtractMessageType(typeof(RequeueConsumer)));
        }

        [Fact]
        public void ExtractMessageType_非消费者返回null()
        {
            Assert.Null(NanaRegister.ExtractMessageType(typeof(TestApexEvent)));
        }

        [Fact]
        public void ScanConsumerTypes_空列表返回空()
        {
            Assert.Empty(NanaRegister.ScanConsumerTypes([]));
            Assert.Empty(NanaRegister.ScanConsumerTypes(null!));
        }

        [Fact]
        public void ScanConsumerTypes_扫描到消费者()
        {
            var filter = new FilterTypeOptions
            {
                AssemblyName = "Viv.Nana.Tests",
                BaseType = typeof(VivConsumer<>).AssemblyQualifiedName
            };

            var types = NanaRegister.ScanConsumerTypes([filter]);

            Assert.Contains(typeof(SuccessConsumer), types);
            Assert.Contains(typeof(RequeueConsumer), types);
        }
    }
}
