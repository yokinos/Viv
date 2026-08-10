using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;
using Viv.Delusion.Magic;
using Viv.Nana.Options;

namespace Viv.Nana.Core
{
    /// <summary>
    /// Wolverine 消息总线配置扩展 — 在 <c>AddViv()</c> 中通过 <c>services.AddVivWolverine(...)</c> 调用。
    /// 职责：RabbitMQ 传输 + 消费方队列监听 + 发布方路由 + 全局失败策略 + EF Saga 持久化。
    /// 替代 MassTransit 的 AddVivMassTransit（License 限制），对外抽象 IVivEventPublisher / VivConsumer 不变。
    /// </summary>
    public static class VivWolverineConfigurationExtensions
    {
        /// <summary>
        /// 消费服务名（入口程序集名）。发布订阅队列 {EventName}Queue.{ServiceName} 的唯一后缀，
        /// 保证不同服务各建一条队列、各自收一份；同一服务多实例共享队列（轮询）。
        /// </summary>
        private static readonly string ServiceName =
            Assembly.GetEntryAssembly()?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName ?? "app";

        public static IServiceCollection AddVivWolverine(
            this IServiceCollection services,
            NanaOptions nanaOptions,
            List<Type>? sagaTypes)
        {
            services.AddWolverine(opts =>
            {
                // 1) RabbitMQ 传输：连接配置 + 自动声明队列/交换机
                //    UseRabbitMq(Uri) 由 URI 内部构建 ConnectionFactory（避免 ConfigureConnection 拿到空实例）
                var vhostPath = nanaOptions.VirtualHost.TrimStart('/');
                var rabbitUri = new Uri(
                    $"amqp://{Uri.EscapeDataString(nanaOptions.UserName)}:{Uri.EscapeDataString(nanaOptions.Password)}" +
                    $"@{nanaOptions.Host}:{nanaOptions.Port}/{vhostPath}");
                var transport = opts.UseRabbitMq(rabbitUri)
                    .AutoProvision();

                // 2) 消费方：发布订阅拓扑——每服务一条独立队列绑到 {EventName}Exchange（fanout 广播）
                //    每个订阅服务各收一份；"只执行一次"由业务层拿分布式锁保证（拿到执行，拿不到丢弃）
                foreach (var consumerType in NanaRegister.ScanConsumerTypes(nanaOptions.ConsumerTypes))
                {
                    opts.Discovery.IncludeType(consumerType);

                    var messageType = NanaRegister.ExtractMessageType(consumerType);
                    if (messageType == null) continue;

                    var exchangeName = NanaRegister.GetExchangeName(messageType);
                    var queueName = NanaRegister.GetConsumerQueueName(messageType, ServiceName);

                    var listener = opts.ListenToRabbitQueue(queueName);
                    transport.BindExchange(exchangeName, ExchangeType.Fanout).ToQueue(queueName);

                    // 消费并发/预取调优：直接写 RabbitMqQueue 属性。
                    // 注意：该 fork 的 fluent PreFetchCount/ListenerCount/QueueType 是空壳（编译通过但不落盘），必须直写。
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

                // 4) 全局失败策略：重试 RetryCount 次（间隔 1s）→ 死信队列
                //    对齐 MassTransit UseMessageRetry(Interval(RetryCount, 1s)) → DLQ；
                //    VivRequeueException（消费者要求重投）走同一路径。
                var retryTimes = Enumerable.Repeat(TimeSpan.FromSeconds(1), Math.Max(1, nanaOptions.RetryCount)).ToArray();
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
    }
}
