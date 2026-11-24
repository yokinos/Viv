using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Viv.Elysia.Extension;
using Viv.Elysia.Logging;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// 注册侧的行为契约。<see cref="IOperationLogWriter"/> 是"实现即接入"的约定，
    /// 所以注册错了（漏注册、重复注册、想关掉默认出口）不会有编译错误，只能在这里钉住。
    /// </summary>
    public class OperationLogRegistrationTests
    {
        private sealed class StubWriter : IOperationLogWriter
        {
            public ValueTask WriteAsync(OperationLogRecord record, CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;
        }

        private static ServiceProvider Build(Action<IServiceCollection>? extra = null, Action<OperationLogOptions>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddVivOperationLog(configure);
            extra?.Invoke(services);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void 默认_注册事件出口与业务补充入口()
        {
            using var provider = Build();

            Assert.Single(provider.GetServices<IOperationLogWriter>());
            Assert.NotNull(provider.GetService<IOperationLogScope>());
            Assert.NotNull(provider.GetService<OperationLogOptions>());
        }

        [Fact]
        public void 关掉事件出口_只留自定义实现()
        {
            using var provider = Build(
                services => services.AddSingleton<IOperationLogWriter, StubWriter>(),
                o => o.UseEventPublisher = false);

            var writers = provider.GetServices<IOperationLogWriter>().ToArray();
            Assert.Single(writers);
            Assert.IsType<StubWriter>(writers[0]);
        }

        [Fact]
        public void 自定义实现与默认出口并存_两个都执行()
        {
            using var provider = Build(services => services.AddSingleton<IOperationLogWriter, StubWriter>());

            var writers = provider.GetServices<IOperationLogWriter>().ToArray();
            Assert.Equal(2, writers.Length);
            Assert.Contains(writers, w => w is StubWriter);
            Assert.Contains(writers, w => w.GetType().Name == "EventOperationLogWriter");
        }

        [Fact]
        public void 重复调用不会重复注册()
        {
            using var provider = Build(services => services.AddVivOperationLog());

            Assert.Single(provider.GetServices<IOperationLogWriter>());
        }

        [Fact]
        public void 配置回调里设的策略能读到()
        {
            using var provider = Build(configure: o =>
            {
                o.RecordFailure = false;
                o.MaxPayloadChars = 128;
            });

            var options = provider.GetRequiredService<OperationLogOptions>();
            Assert.False(options.RecordFailure);
            Assert.Equal(128, options.MaxPayloadChars);
        }
    }
}
