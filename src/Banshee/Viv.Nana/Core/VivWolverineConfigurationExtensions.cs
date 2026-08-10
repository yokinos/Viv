using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence;
using Wolverine.RabbitMQ;
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
                opts.UseRabbitMq(rabbitUri)
                    .AutoProvision();

                // 2) 消费方：显式注册消费者类型 + 监听 {EventName}Queue
                foreach (var consumerType in NanaRegister.ScanConsumerTypes(nanaOptions.ConsumerTypes))
                {
                    opts.Discovery.IncludeType(consumerType);

                    var messageType = NanaRegister.ExtractMessageType(consumerType);
                    if (messageType != null)
                        opts.ListenToRabbitQueue(NanaRegister.GetQueueName(messageType));
                }

                // 3) 发布路由：所有 NanaEnvelope<T> → {EventName}Queue
                //    跨服务队列名一致（NanaRegister.GetQueueName 约定），AutoProvision 自动建队列
                foreach (var eventType in TypeScanMagic.ScanTypes<NanaEvent>())
                {
                    var envelopeType = typeof(NanaEnvelope<>).MakeGenericType(eventType);
                    opts.PublishMessage(envelopeType)
                        .ToRabbitQueue(NanaRegister.GetQueueName(eventType));
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
