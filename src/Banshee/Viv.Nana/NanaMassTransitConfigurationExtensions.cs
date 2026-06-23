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

                if (!nanaOptions.ConsumerTypes.IsNullOrEmpty())
                    NanaRegister.AddVivConsumers(x, nanaOptions.ConsumerTypes);

                if (!sagaStateMachineTypes.IsNullOrEmpty())
                    NanaRegister.AddVivSagas(x, sagaStateMachineTypes);

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

                    // Exchange = NanaMessage<T> 中 T 的命名空间
                    cfg.MessageTopology.SetEntityNameFormatter(VivEntityNameFormatter.Instance);

                    // Queue = {Name}Queue，Saga 走默认
                    cfg.ConfigureEndpoints(context, VivNanaEndpointNameFormatter.Instance);
                });
            });

            services.AddScoped<IVivPublisher, NanaEventPublisher>();

            return services;
        }
    }
}
