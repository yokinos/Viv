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
