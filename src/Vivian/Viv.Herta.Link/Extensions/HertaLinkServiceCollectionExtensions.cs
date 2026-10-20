using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Herta.Link.Dispatch;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Options;

namespace Viv.Herta.Link.Extensions
{
    public static class HertaLinkServiceCollectionExtensions
    {
        public static IServiceCollection AddHertaLink(this IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection(nameof(HertaLinkOptions)).Get<HertaLinkOptions>() ?? new HertaLinkOptions();

            services.Configure<HertaLinkOptions>(configuration.GetSection(nameof(HertaLinkOptions)));
            services.TryAddSingleton<IConnectionPool, ConnectionPool>();
            services.TryAddSingleton<IFanoutPlanner, FanoutPlanner>();
            services.TryAddScoped<IMessageDispatcher, SignalRMessageDispatcher>();
            services.TryAddScoped<IConnectionCoordinator, ConnectionCoordinator>();
            services.TryAddScoped<IMembershipSync, MembershipSync>();
            services.AddHostedService<ConnectionLeaseSweeper>();

            services.AddSignalR(o =>
            {
                o.EnableDetailedErrors = options.EnableDetailedErrors;
            });

            return services;
        }
    }
}
