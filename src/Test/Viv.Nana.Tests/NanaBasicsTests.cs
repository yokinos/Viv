using Viv.Contracts.Models;
using Viv.Momo.Enums;
using Viv.Nana.Core;
using Viv.Nana.Options;

namespace Viv.Nana.Tests
{
    public class SubscribeResultTests
    {
        [Fact]
        public void Success_成功不重投()
        {
            var r = SubscribeResult.Success();
            Assert.True(r.IsSuccess);
            Assert.False(r.IsRequeue);
        }

        [Fact]
        public void Fail_可重投()
        {
            var r = SubscribeResult.Failed(true, "boom");
            Assert.False(r.IsSuccess);
            Assert.True(r.IsRequeue);
            Assert.Equal("boom", r.Message);
        }

        [Fact]
        public void Fail_不可重投()
        {
            var r = SubscribeResult.Failed(false, "skip");
            Assert.False(r.IsSuccess);
            Assert.False(r.IsRequeue);
            Assert.Equal("skip", r.Message);
        }
    }

    public class NanaEnvelopeTests
    {
        [Fact]
        public void 默认MessageId非零()
        {
            var e = new NanaEnvelope<TestApexEvent>();
            Assert.NotEqual(0L, e.MessageId);
        }

        [Fact]
        public void CreatedAt接近当前时间()
        {
            var before = DateTimeOffset.UtcNow.AddSeconds(-10);
            var e = new NanaEnvelope<TestApexEvent>();
            var after = DateTimeOffset.UtcNow.AddSeconds(10);

            Assert.InRange(e.CreatedAt, before, after);
        }

        [Fact]
        public void 可设置内容与上下文()
        {
            var e = new NanaEnvelope<TestApexEvent>
            {
                Content = new TestApexEvent { Payload = "x" },
                Context = new VivContextContent { AppId = 1, SubjectId = 3, UserId = 2, HolderId = "h-1" }
            };

            Assert.Equal("x", e.Content?.Payload);
            Assert.Equal(2, e.Context?.UserId);
            Assert.Equal(3, e.Context?.SubjectId);
            Assert.Equal("h-1", e.Context?.HolderId);
        }
    }

    public class VivRequeueExceptionTests
    {
        [Fact]
        public void 消息传递()
        {
            var ex = new VivRequeueException("boom");
            Assert.Equal("boom", ex.Message);
        }

        [Fact]
        public void 内部异常传递()
        {
            var inner = new InvalidOperationException("inner");
            var ex = new VivRequeueException("boom", inner);
            Assert.Same(inner, ex.InnerException);
        }
    }

    public class NanaConsumerAttributeTests
    {
        [Fact]
        public void 默认预取数为20()
        {
            Assert.Equal((ushort)20, NanaConsumerAttribute.DefaultPrefetchCount);
        }

        [Fact]
        public void 新特性各字段默认零()
        {
            var attr = new NanaConsumerAttribute();
            Assert.Equal(0, attr.ConsumerCount);
            Assert.Equal((ushort)0, attr.PrefetchCount);
            Assert.Equal(0, attr.MaximumParallelMessages);
        }
    }

    public class NanaOptionsTests
    {
        [Fact]
        public void 默认连接配置()
        {
            var o = new NanaOptions();
            Assert.Equal("localhost", o.Host);
            Assert.Equal(5672, o.Port);
            Assert.Equal("guest", o.UserName);
            Assert.Equal("guest", o.Password);
            Assert.Equal("/", o.VirtualHost);
            Assert.Equal(3, o.RetryCount);
            Assert.Empty(o.ConsumerTypes);
            Assert.Equal(DatabaseSourceType.PostgreSQL, o.SagaDatabaseSource);
            Assert.Null(o.SagaConnectionString);
        }
    }
}
