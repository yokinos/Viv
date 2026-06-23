using MassTransit;
using Viv.Delusion;
using Viv.Nana.Core;
using Viv.Nana.Options;
using Viv.Nana.Saga;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;

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
        /// 扫描并注册消费者，返回注册的类型列表
        /// </summary>
        public static List<Type> AddVivConsumers(
            IBusRegistrationConfigurator configurator,
            List<FilterTypeOptions> consumerTypes)
        {
            var result = new List<Type>();

            if (consumerTypes.IsNullOrEmpty()) return result;

            var types = TypeScanMagic.ScanRange(consumerTypes);
            if (types.IsNullOrEmpty()) return result;

            var registerMethod = typeof(NanaRegister)
                .GetMethod(nameof(RegisterConsumer), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            foreach (var type in types)
            {
                registerMethod?.MakeGenericMethod(type).Invoke(null, [configurator]);
                result.Add(type);
            }

            return result;
        }

        private static void RegisterConsumer<TConsumer>(IBusRegistrationConfigurator configurator)
            where TConsumer : class, IConsumer
        {
            configurator.AddConsumer<TConsumer>();
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

        /// <summary>
        /// 注册 Saga 状态机（类型由 IVivSagaStateMachine 接口扫描得到）
        /// </summary>
        public static List<Type> AddVivSagas(
            IBusRegistrationConfigurator configurator,
            List<Type> stateMachineTypes)
        {
            var result = new List<Type>();

            if (stateMachineTypes.IsNullOrEmpty()) return result;

            foreach (var smType in stateMachineTypes)
            {
                var stateType = VivSagaRegistrationHelper.ExtractStateType(smType);
                if (stateType == null) continue;

                VivSagaRegistrationHelper.RegisterSaga(configurator, smType, stateType);
                result.Add(smType);
            }

            return result;
        }
    }
}
