using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Engine;
using Viv.Herta.Core.IService;
using Viv.Herta.Link.Hubs;
using Viv.Herta.Link.Options;
using Viv.Herta.Link.Services;

namespace Viv.Herta.Link.Extensions
{
    public static class HertaLinkServiceCollectionExtensions
    {
        public static IServiceCollection AddHertaLink(this IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection(nameof(HertaLinkOptions)).Get<HertaLinkOptions>() ?? new HertaLinkOptions();

            services.Configure<HertaLinkOptions>(configuration.GetSection(nameof(HertaLinkOptions)));
            services.TryAddSingleton<IConnectionPool, ConnectionPool>();
            services.TryAddScoped<IGroupService, DefaultGroupService>();

            var signalR = services.AddSignalR(o =>
            {
                o.EnableDetailedErrors = options.EnableDetailedErrors;
            });

            signalR.AddStackExchangeRedis(VivEngine.VivOptions.CacheOption.RedisOptions.ConnectionString);
            return services;
        }
    }
}
