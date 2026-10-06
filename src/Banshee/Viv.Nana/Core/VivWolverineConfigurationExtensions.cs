using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;
using Viv.Nana.Options;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

namespace Viv.Nana.Core
{
    /// <summary>
    /// Wolverine 消息总线配置扩展 — 在 <c>AddViv()</c> 中通过 <c>services.AddVivWolverine(...)</c> 调用。
    /// 职责：RabbitMQ 传输 + 消费方队列监听 + 发布方路由 + 全局失败策略 + EF Saga 持久化。
    /// </summary>
    public static class VivWolverineConfigurationExtensions
    {
        /// <summary>
        /// 服务定位白名单的默认项。
        ///
        /// 这几个契约的实现是 internal，生成的 handler 代码在另一个程序集里编译，
        /// 写不出 <c>new LocalEventBus(...)</c> 这类构造，只能从作用域容器取，所以逐条列在这里。
        /// 本程序集之外的同类实现（如 <c>Viv.Outbox</c> 的 <c>IVivOutbox</c>）由调用方经
        /// <c>extraServiceLocationTypes</c> 传入 —— <c>Viv.Outbox → Viv.Nana</c> 是单向引用，这里写不出来。
        /// </summary>
        private static readonly Type[] DefaultServiceLocationTypes =
        [
            typeof(IVivLocalEventBus),
            typeof(IVivUnitOfWork),
            typeof(IVivInbox)
        ];

        public static IServiceCollection AddVivWolverine(this IServiceCollection services, NanaOptions nanaOptions, List<Type>? sagaTypes, IReadOnlyList<Type>? extraServiceLocationTypes = null)
        {
            services.AddWolverine(opts =>
            {
                // 0) 服务定位白名单。Wolverine 默认 ServiceLocationPolicy.NotAllowed：
                //    生成代码构造不出来的依赖直接报错，不再退回服务定位。
                //    上面那三个契约的实现是 internal，只能从作用域容器取，所以逐条点名放行。
                //    名单必须齐：漏一个，该 handler 的消息会全部进死信，异常里会写出缺哪个类型，照着补一行即可。
                //    其余项目里的同类实现由 extraServiceLocationTypes 传进来。
                opts.ServiceLocationPolicy = ServiceLocationPolicy.NotAllowed;
                foreach (var serviceType in DefaultServiceLocationTypes)
                    opts.CodeGeneration.AlwaysUseServiceLocationFor(serviceType);
                if (extraServiceLocationTypes is not null)
                    foreach (var serviceType in extraServiceLocationTypes)
                        opts.CodeGeneration.AlwaysUseServiceLocationFor(serviceType);

                // 1) RabbitMQ 传输：连接配置 + 自动声明队列/交换机
                //    UseRabbitMq(Uri) 由 URI 内部构建 ConnectionFactory（避免 ConfigureConnection 拿到空实例）
                var vhostPath = nanaOptions.VirtualHost.TrimStart('/');
                var rabbitUri = new Uri(
                    $"amqp://{Uri.EscapeDataString(nanaOptions.UserName)}:{Uri.EscapeDataString(nanaOptions.Password)}" +
                    $"@{nanaOptions.Host}:{nanaOptions.Port}/{vhostPath}");
                var transport = opts.UseRabbitMq(rabbitUri)
                    .AutoProvision();

                // 2) 消费方：发布订阅拓扑——每服务一条独立队列绑到 {EventName}Exchange（fanout 广播）
                //    每个订阅服务各收一份；同服务只执行一次由 VivConsumer 基类取 Redis 锁（拿到进业务，拿不到丢弃）
                // 本地队列消费者（VivLocalConsumer<T>）：事件类型 → 消费者类型。
                // 供 3b) 查 [NanaConsumer] 与判定孤儿事件用，不在这里注册 RabbitMQ 拓扑。
                var localConsumers = new Dictionary<Type, Type>();

                foreach (var consumerType in NanaRegister.ScanConsumerTypes(nanaOptions.ConsumerTypes))
                {
                    opts.Discovery.IncludeType(consumerType);

                    var messageType = NanaRegister.ExtractMessageType(consumerType);
                    if (messageType == null)
                    {
                        // 不是 RabbitMQ 消费者。若是 VivLocalConsumer<T>，登记给 3b)。
                        // 注意 Discovery.IncludeType 在上面已经调过，Wolverine 照样能发现它的 HandleAsync ——
                        // 本地消费者的发现路径与 RabbitMQ 消费者完全一致，只有队列拓扑不同。
                        var localEventType = NanaRegister.ExtractLocalMessageType(consumerType);
                        if (localEventType != null) localConsumers[localEventType] = consumerType;
                        continue;
                    }

                    var exchangeName = NanaRegister.GetExchangeName(messageType);
                    var queueName = NanaRegister.GetConsumerQueueName(messageType, NanaRegister.CurrentServiceName);

                    var listener = opts.ListenToRabbitQueue(queueName);
                    transport.BindExchange(exchangeName, ExchangeType.Fanout).ToQueue(queueName);

                    // 消费并发/预取调优：直接写 RabbitMqQueue 属性。
                    // 官方包的 fluent PreFetchCount/ListenerCount/QueueType/MaximumParallelMessages 都是空壳
                    // （6.42.0 实测：调用不抛异常，队列属性不变），必须直写。
                    // 默认 prefetch=20（比 Wolverine 原生 100 更低的重投放大）、队列 Quorum（多副本防丢消息）；
                    // ConsumerCount/MaximumParallelMessages 由 [NanaConsumer] 特性显式指定。
                    var attr = consumerType.GetCustomAttribute<NanaConsumerAttribute>();
                    if (listener.Endpoint is RabbitMqQueue queue)
                    {
                        queue.PreFetchCount = attr?.PrefetchCount > 0 ? attr.PrefetchCount : NanaConsumerAttribute.DefaultPrefetchCount;
                        queue.QueueType = QueueType.quorum;
                        if (attr?.ConsumerCount > 0) queue.ListenerCount = attr.ConsumerCount;
                        if (attr?.MaximumParallelMessages > 0) queue.MaxDegreeOfParallelism = attr.MaximumParallelMessages;
                    }
                }

                // 3) 发布路由：所有 NanaEnvelope<T> → {EventName}Exchange（fanout 交换机）
                //    发布侧同样显式声明 fanout 类型，与消费侧 BindExchange 一致（避免 406 PRECONDITION_FAILED）
                foreach (var eventType in TypeScanMagic.ScanTypes<NanaEvent>())
                {
                    var exchangeName = NanaRegister.GetExchangeName(eventType);
                    var envelopeType = typeof(NanaEnvelope<>).MakeGenericType(eventType);

                    transport.DeclareExchange(exchangeName, ex => ex.ExchangeType = ExchangeType.Fanout);
                    opts.PublishMessage(envelopeType).ToRabbitExchange(exchangeName);
                }

                // 3b) 本地队列拓扑：所有 NanaLocalEvent → 进程内本地队列（与 3) 的出网语义相对）
                //     与 3) 对称：同样按事件类型逐条声明端点 + 注册路由，只是端点换成 local（Wolverine LocalTransport）。
                //     NanaLocalEvent 不是 NanaEvent 子类，3) 的 ScanTypes<NanaEvent>() 扫不到它，两条线互不干扰；
                //     反过来说——它一旦继承 NanaEvent，这里和 3) 都会注册，发布即双发。由 NanaLocalEventTests 守住。
                //     已实测：PascalCase 队列名在 LocalQueue(name) 与 ToLocalQueue(name) 之间能对上，端到端投递正常。
                var localEventTypes = TypeScanMagic.ScanTypes<NanaLocalEvent>();
                var orphanLocalEvents = new List<string>();

                foreach (var eventType in localEventTypes)
                {
                    var queueName = NanaRegister.GetLocalQueueName(eventType);
                    var envelopeType = typeof(NanaLocalEnvelope<>).MakeGenericType(eventType);

                    var localQueue = opts.LocalQueue(queueName);
                    opts.PublishMessage(envelopeType).ToLocalQueue(queueName);

                    // 本地消费者同样吃 [NanaConsumer]，但只有 MaximumParallelMessages 有意义 ——
                    // PrefetchCount / ConsumerCount 是 RabbitMQ 概念，本地队列既没有预取也没有多监听器。
                    if (localConsumers.TryGetValue(eventType, out var localConsumerType))
                    {
                        var localAttr = localConsumerType.GetCustomAttribute<NanaConsumerAttribute>();
                        if (localAttr?.MaximumParallelMessages > 0)
                            localQueue.MaximumParallelMessages(localAttr.MaximumParallelMessages);
                    }
                    else
                    {
                        // 无消费者：消息进队列后无人处理（本地队列没有 fanout 无绑定队列即丢弃的兜底）
                        orphanLocalEvents.Add($"{eventType.Name} → {queueName}");
                    }
                }

                NanaRegister.RecordLocalQueueScan(localEventTypes.Count, orphanLocalEvents);

                // 4) 全局失败策略：使用指数退避重试（基础延迟5s，最大60s，带抖动），重试次数由配置 RetryCount 决定
                //    重试全部失败后移入死信队列（DLQ）
                //    VivRequeueException（消费者要求重投）也走同一重试路径
                var retryTimes = GenerateExponentialBackoff(Math.Max(1, nanaOptions.RetryCount), 5 * 1000, 60 * 1000);
                opts.Policies.OnException<Exception>()
                    .RetryWithCooldown(retryTimes)
                    .Then
                    .MoveToErrorQueue();

                // 5) EF Saga 持久化（SagaConnectionString 已配且扫到 saga 类型才启用）
                if (sagaTypes is { Count: > 0 })
                {
                    // Lightweight = 无 durable outbox：saga 状态变更直接走 DbContext 事务，
                    // 不要求数据库消息持久化（Eager 默认会要求，导致 "not using Database backed message persistence"）。
                    opts.UseEntityFrameworkCoreTransactions(TransactionMiddlewareMode.Lightweight);
                    foreach (var sagaType in sagaTypes)
                        opts.Discovery.IncludeType(sagaType);
                }
            });

            return services;
        }

        /// <summary>
        /// 生成指数退避时间集合（带随机抖动）
        /// </summary>
        /// <param name="retryCount">重试次数</param>
        /// <param name="baseDelay">基础延迟（毫秒），默认 200</param>
        /// <param name="maxDelay">最大延迟（毫秒），默认 5000</param>
        /// <returns>指数退避时间集合</returns>
        private static TimeSpan[] GenerateExponentialBackoff(int retryCount, int baseDelay = 200, int maxDelay = 5000)
        {
            if (retryCount <= 0)
                return [];

            var result = new TimeSpan[retryCount];

            for (int i = 0; i < retryCount; i++)
            {
                // 指数增长：baseDelay * 2^i
                var delay = baseDelay * (int)Math.Pow(2, i);
                delay = Math.Min(delay, maxDelay);
                // 随机抖动 0-30%，防止惊群
                var jitter = RandomMagic.Next(0, (int)(delay * 0.3));
                result[i] = TimeSpan.FromMilliseconds(delay + jitter);
            }

            return result;
        }
    }
}
