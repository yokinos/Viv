using System.Reflection;
using Viv.Contracts;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Nana.Options;

namespace Viv.Nana
{
    public static class NanaRegister
    {
        /// <summary>
        /// 扫描并返回消费者类型清单（供 Wolverine 显式 IncludeType + ListenToRabbitQueue 注册）
        /// </summary>
        public static List<Type> ScanConsumerTypes(List<FilterTypeOptions> consumerTypes)
        {
            if (consumerTypes.IsNullOrEmpty()) return [];

            return TypeScanMagic.ScanRange(consumerTypes);
        }

        /// <summary>
        /// 生成 Queue 名称：{EventName}Queue（去 Event 后缀）
        /// TestApexEvent → TestApexQueue
        /// </summary>
        public static string GetQueueName(Type messageType) => $"{StripEventSuffix(messageType)}Queue";

        /// <summary>
        /// 生成交换机名称：{EventName}Exchange（fanout 广播，发布订阅语义）
        /// TestApexEvent → TestApexExchange
        /// </summary>
        public static string GetExchangeName(Type messageType) => $"{StripEventSuffix(messageType)}Exchange";

        /// <summary>
        /// 生成本地队列名称：{EventName}LocalQueue（去 Event 后缀）
        /// TestApexEvent → TestApexLocalQueue
        /// 进程内点对点队列（Wolverine LocalTransport），与 {EventName}Exchange 的跨进程 fanout 语义相对。
        /// </summary>
        public static string GetLocalQueueName(Type messageType) => $"{StripEventSuffix(messageType)}LocalQueue";

        /// <summary>
        /// 去 Event 后缀（大小写不敏感）：TestApexEvent → TestApex，PlainMessage → PlainMessage
        /// </summary>
        private static string StripEventSuffix(Type messageType)
        {
            var name = messageType.Name;
            if (name.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
                name = name[..^5];
            return name;
        }

        /// <summary>
        /// 生成消费方队列名称：{EventName}Queue.{ServiceName}
        /// 发布订阅拓扑：每个消费服务建一条独立队列绑到 {EventName}Exchange，各自收一份；
        /// 同一服务多实例共享同一队列（RabbitMQ 轮询分派）。同服务只执行一次由
        /// <see cref="VivConsumer{T}"/> 基类按 <see cref="GetConsumerLockKey"/> 取锁保证。
        /// </summary>
        public static string GetConsumerQueueName(Type messageType, string serviceName)
        {
            return $"{GetQueueName(messageType)}.{serviceName}";
        }

        /// <summary>
        /// 消费服务名（入口程序集名）。队列后缀与消费锁 Key 共用，保证 fanout 下各服务各持一把锁。
        /// </summary>
        public static string CurrentServiceName { get; } =
            Assembly.GetEntryAssembly()?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName ?? "app";

        /// <summary>
        /// 注册期扫到的本地事件（<see cref="NanaLocalEvent"/> 子类）数量。
        /// 供 <c>NanaLocalEventPublisher</c> 首次构造时打一条启动日志 —— 注册期 VivLocator 尚未 Initialize，
        /// 拿不到 ILoggerContract，只能把结论存下来延后输出（与 Viv.Engine 的 LocalEventRegistration 同一手法）。
        /// </summary>
        public static int LocalEventTypeCount { get; private set; }

        /// <summary>
        /// 有事件类型、却扫不到消费者的本地事件（元素形如 "XxxEvent → XxxLocalQueue"）。
        /// 本地队列没有 RabbitMQ 那种「fanout 无绑定队列即丢弃」的兜底，无消费者就是消息进队列后无人处理，
        /// 启动时必须留痕，绝不静默吞。
        /// </summary>
        public static IReadOnlyList<string> OrphanLocalEvents { get; private set; } = [];

        /// <summary>
        /// 记录本地队列拓扑的扫描结果（只由 <c>AddVivWolverine</c> 的 3b) 段调用）
        /// </summary>
        public static void RecordLocalQueueScan(int eventTypeCount, IReadOnlyList<string> orphanLocalEvents)
        {
            LocalEventTypeCount = eventTypeCount;
            OrphanLocalEvents = orphanLocalEvents;
        }

        /// <summary>
        /// 框架消费锁 Key：<c>nana:{ServiceName}:{EventType}:{MessageId}</c>。
        /// 含服务名，避免 Apex / DeepRed 抢同一把锁导致只有一个服务进业务。
        /// </summary>
        public static string GetConsumerLockKey(string eventTypeName, long messageId)
        {
            return LockKeyMagic.Join(LockKeyMagic.NanaPrefix, CurrentServiceName, eventTypeName, messageId);
        }

        /// <summary>
        /// 从 VivConsumer&lt;T&gt; 提取消息类型 T
        /// </summary>
        public static Type? ExtractMessageType(Type consumerType)
            => ExtractBaseChainGenericArgument(consumerType, typeof(VivConsumer<>));

        /// <summary>
        /// 从 VivLocalConsumer&lt;T&gt; 提取本地事件类型 T（与 <see cref="ExtractMessageType"/> 对称）。
        /// 返回 null 表示不是本地队列消费者。
        /// </summary>
        public static Type? ExtractLocalMessageType(Type consumerType)
            => ExtractBaseChainGenericArgument(consumerType, typeof(VivLocalConsumer<>));

        /// <summary>
        /// 沿基类链找 openGeneric（如 <c>VivConsumer&lt;&gt;</c>）并返回其闭合泛型实参
        /// </summary>
        private static Type? ExtractBaseChainGenericArgument(Type consumerType, Type openGeneric)
        {
            var baseType = consumerType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType
                    && baseType.GetGenericTypeDefinition() == openGeneric)
                {
                    return baseType.GetGenericArguments()[0];
                }
                baseType = baseType.BaseType;
            }
            return null;
        }
    }
}
