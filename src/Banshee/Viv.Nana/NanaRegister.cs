using MassTransit;
using Viv.Nana.Core;
using Viv.Nana.Options;
using Viv.Nana.Saga;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

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
        /// 扫描并注册 Saga 状态机（MassTransit StateMachine + EF Core 持久化）
        /// </summary>
        public static void AddVivSagas(
            IBusRegistrationConfigurator configurator,
            List<FilterTypeOptions> stateMachineTypes)
        {
            if (stateMachineTypes.IsNullOrEmpty()) return;

            var types = TypeScanMagic.ScanRange(stateMachineTypes);
            if (types.IsNullOrEmpty()) return;

            foreach (var smType in types)
            {
                var stateType = VivSagaRegistrationHelper.ExtractStateType(smType);
                if (stateType == null) continue;

                VivSagaRegistrationHelper.RegisterSaga(configurator, smType, stateType);
            }
        }
    }
}
