using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Viv.Nana.Core;
using Viv.Nana.Options;
using Viv.Delusion.Extension;

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
                x.AddDelayedMessageScheduler();

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.UseDelayedMessageScheduler();

                    cfg.Host(new Uri(rabbitUri), h =>
                    {
                        h.Username(nanaOptions.UserName);
                        h.Password(nanaOptions.Password);
                    });

                    cfg.UseMessageRetry(r =>
                        r.Interval(nanaOptions.RetryCount, TimeSpan.FromSeconds(1)));

                    cfg.ConfigureEndpoints(context);
                });

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
            });

            return services;
        }
    }
}
