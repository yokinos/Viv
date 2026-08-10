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
