using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Nana.Core;
using Viv.Nana.Options;

namespace Viv.Nana
{
    public static class NanaRegister
    {
        public static void Initialize(NanaOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            VivConfigRegistry.Add(options);
        }

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
        public static string GetQueueName(Type messageType)
        {
            var name = messageType.Name;
            if (name.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
                name = name[..^5];
            return $"{name}Queue";
        }

        /// <summary>
        /// 生成交换机名称：{EventName}Exchange（fanout 广播，发布订阅语义）
        /// TestApexEvent → TestApexExchange
        /// </summary>
        public static string GetExchangeName(Type messageType)
        {
            var name = messageType.Name;
            if (name.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
                name = name[..^5];
            return $"{name}Exchange";
        }

        /// <summary>
        /// 生成消费方队列名称：{EventName}Queue.{ServiceName}
        /// 发布订阅拓扑：每个消费服务建一条独立队列绑到 {EventName}Exchange，各自收一份；
        /// 同一服务多实例共享同一队列（RabbitMQ 轮询分派），"只执行一次"由业务层分布式锁保证。
        /// </summary>
        public static string GetConsumerQueueName(Type messageType, string serviceName)
        {
            return $"{GetQueueName(messageType)}.{serviceName}";
        }

        /// <summary>
        /// 从 VivConsumer&lt;T&gt; 提取消息类型 T
        /// </summary>
        public static Type? ExtractMessageType(Type consumerType)
        {
            var baseType = consumerType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType
                    && baseType.GetGenericTypeDefinition() == typeof(VivConsumer<>))
                {
                    return baseType.GetGenericArguments()[0];
                }
                baseType = baseType.BaseType;
            }
            return null;
        }
    }
}
