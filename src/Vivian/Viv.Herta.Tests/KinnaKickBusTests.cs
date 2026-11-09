using System.Reflection;
using Viv.Fakes;
using Viv.Herta.Link.Adapters;
using Viv.Kinna.Contracts;
using Viv.Kinna.Ports;
using Viv.Log;
using Viv.Redis;

namespace Viv.Herta.Tests
{
    /// <summary>
    /// 跨副本踢人通道的 Redis 实现。重点只有一条：<see cref="IKinnaKickBus.Subscribe"/> 的
    /// 回调不并发 —— Redis 的接收线程会连着回调，直接 `_ = handler(...)` 会让两条指令
    /// 同时进协调器，同一个用户的连接被摘两次、软踢帧交错写进待发队列。
    /// </summary>
    public class KinnaKickBusTests
    {
        [Fact]
        public async Task 并发投递的踢人指令串行处理()
        {
            var fake = CreateRedis(out var redis);
            var bus = new RedisKinnaKickBus(redis, new RecordingLogger());

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var gate = new object();
            var running = 0;
            var maxRunning = 0;
            var processed = 0;

            using var subscription = bus.Subscribe(_ =>
            {
                lock (gate)
                {
                    running++;
                    maxRunning = Math.Max(maxRunning, running);
                }

                entered.TrySetResult();

                return HandleAsync();

                async Task HandleAsync()
                {
                    try
                    {
                        await release.Task.ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (gate)
                        {
                            running--;
                            processed++;
                        }
                    }
                }
            });

            var handler = Assert.Single(fake.Handlers);

            // 模拟 Redis 接收线程上连着来两条：第一次回调还没返回，第二条就到了
            handler(new KinnaKickCommand { SubjectId = 1, UserId = 2 });
            handler(new KinnaKickCommand { SubjectId = 1, UserId = 3 });

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // 第二条必须还排在队列里：并发的话这里会读到 2
            lock (gate)
            {
                Assert.Equal(1, running);
                Assert.Equal(1, maxRunning);
            }

            release.SetResult();

            await WaitUntilAsync(() => Volatile.Read(ref processed) == 2, "两条指令都处理完");

            lock (gate)
            {
                // 峰值一直是 1：串行化真的生效了，不是碰巧错开的
                Assert.Equal(1, maxRunning);
            }
        }

        [Fact]
        public async Task 一条指令处理失败不拖垮后续()
        {
            // 消费者是单读者循环：异常从这里冒出去，循环就退出，之后所有踢人指令都收不到
            var fake = CreateRedis(out var redis);
            var logger = new RecordingLogger();
            var bus = new RedisKinnaKickBus(redis, logger);

            var processed = 0;

            using var subscription = bus.Subscribe(command =>
            {
                if (command.UserId == 2)
                {
                    throw new InvalidOperationException("kick failed");
                }

                Interlocked.Increment(ref processed);
                return Task.CompletedTask;
            });

            var handler = Assert.Single(fake.Handlers);

            handler(new KinnaKickCommand { SubjectId = 1, UserId = 2 });
            handler(new KinnaKickCommand { SubjectId = 1, UserId = 3 });

            await WaitUntilAsync(() => Volatile.Read(ref processed) == 1, "第二条照常处理");

            Assert.Contains(logger.Errors, message => message.Contains("user=2", StringComparison.Ordinal));
        }

        [Fact]
        public void 释放后取消订阅()
        {
            var fake = CreateRedis(out var redis);
            var bus = new RedisKinnaKickBus(redis, new RecordingLogger());

            var subscription = bus.Subscribe(_ => Task.CompletedTask);
            subscription.Dispose();
            subscription.Dispose();

            // 幂等：重复 Dispose 不该再退订一次（Unsubscribe 是按频道粒度的，抖两次更容易误伤别人）
            Assert.Equal(1, fake.UnsubscribeCalls);
        }

        private static FakeKickRedis CreateRedis(out IRedisService redis)
        {
            var proxy = DispatchProxy.Create<IRedisService, FakeKickRedis>();
            redis = proxy;
            return (FakeKickRedis)(object)proxy;
        }

        private static async Task WaitUntilAsync(Func<bool> condition, string what)
        {
            for (var i = 0; i < 200; i++)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"等待超时: {what}");
        }
    }

    /// <summary>
    /// Redis 替身：只关心订阅登记下来的那个回调，其余调用交给 <see cref="TestProxy"/> 兜底。
    ///
    /// 不能加 sealed：<see cref="DispatchProxy.Create{T, TProxy}"/> 要派生一个动态类型出来。
    /// </summary>
    public class FakeKickRedis : TestProxy
    {
        /// <summary>登记在频道上的回调，测试用它模拟「Redis 推来一条消息」</summary>
        public List<Action<KinnaKickCommand>> Handlers { get; } = [];

        public int UnsubscribeCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "Subscribe" when args is { Length: 2 } && args[1] is Action<KinnaKickCommand> handler:
                    Handlers.Add(handler);
                    return null;

                case "Unsubscribe":
                    UnsubscribeCalls++;
                    Handlers.Clear();
                    return null;

                default:
                    return base.Invoke(targetMethod, args);
            }
        }
    }
}
