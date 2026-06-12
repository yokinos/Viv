using MassTransit;
using Viv.Delusion;
using Viv.Delusion.Magic;
using Viv.Nana.Core;
using Viv.Nana.Options;
using Viv.Nana.Saga;
using Viv.Delusion.Extension;

namespace Viv.Nana
{
    public static class NanaRegister
    {
        public static void Initialize(NanaOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            VivConfigRegistry.Add(options);
        }

        public static void AddVivConsumers(
            IBusRegistrationConfigurator configurator,
            List<FilterTypeOptions> consumerTypes)
        {
            if (consumerTypes.IsNullOrEmpty()) return;

            var types = TypeScanMagic.ScanRange(consumerTypes);
            if (types.IsNullOrEmpty()) return;

            foreach (var type in types)
            {
                var method = typeof(RegistrationExtensions)
                    .GetMethod(nameof(RegistrationExtensions.AddConsumer), 1, [typeof(IBusRegistrationConfigurator)])
                    ?.MakeGenericMethod(type);

                method?.Invoke(null, [configurator]);
            }
        }

        /// <summary>
        /// 注册 Saga 状态机（类型由 IVivSagaStateMachine 接口扫描得到）
        /// </summary>
        public static void AddVivSagas(
            IBusRegistrationConfigurator configurator,
            List<Type> stateMachineTypes)
        {
            if (stateMachineTypes.IsNullOrEmpty()) return;

            foreach (var smType in stateMachineTypes)
            {
                var stateType = VivSagaRegistrationHelper.ExtractStateType(smType);
                if (stateType == null) continue;

                VivSagaRegistrationHelper.RegisterSaga(configurator, smType, stateType);
            }
        }
    }
}
