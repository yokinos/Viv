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
        public void GetConsumerQueueName_拼接服务名()
        {
            Assert.Equal(
                "TestApexQueue.viv.apex.worker",
                NanaRegister.GetConsumerQueueName(typeof(TestApexEvent), "viv.apex.worker"));
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

        [Fact]
        public void Initialize_存储配置到注册表()
        {
            var opts = new NanaOptions { Host = "rabbit.test" };
            NanaRegister.Initialize(opts);

            Assert.Same(opts, VivConfigRegistry.Get<NanaOptions>());
        }

        [Fact]
        public void Initialize_null_抛异常()
        {
            Assert.Throws<ArgumentNullException>(() => NanaRegister.Initialize(null!));
        }
    }
}
