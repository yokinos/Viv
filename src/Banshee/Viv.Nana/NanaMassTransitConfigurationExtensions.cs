using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Viv.Nana.Core;
using Viv.Nana.Options;
using Viv.Vva.Extension;

namespace Viv.Nana
{
    public static class NanaMassTransitConfigurationExtensions
    {
        public static IServiceCollection AddVivMassTransit(this IServiceCollection services, NanaOptions nanaOptions, List<Type>? sagaStateMachineTypes = null)
        {
            ArgumentNullException.ThrowIfNull(nanaOptions);

            var rabbitUri = $"rabbitmq://{nanaOptions.Host}:{nanaOptions.Port}/{nanaOptions.VirtualHost}";

            services.AddMassTransit(x =>
            {
                // 注册消费者
                if (!nanaOptions.ConsumerTypes.IsNullOrEmpty())
                {
                    NanaRegister.AddVivConsumers(x, nanaOptions.ConsumerTypes);
                }

                // 注册 Saga
                if (!sagaStateMachineTypes.IsNullOrEmpty())
                {
                    NanaRegister.AddVivSagas(x, sagaStateMachineTypes);
                }

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(rabbitUri), h =>
                    {
                        h.Username(nanaOptions.UserName);
                        h.Password(nanaOptions.Password);
                    });

                    cfg.UseDelayedMessageScheduler();

                    cfg.UseMessageRetry(r =>
                        r.Interval(nanaOptions.RetryCount, TimeSpan.FromSeconds(1)));

                    cfg.ConfigureEndpoints(context);
                });
            });

            return services;
        }
    }
}
