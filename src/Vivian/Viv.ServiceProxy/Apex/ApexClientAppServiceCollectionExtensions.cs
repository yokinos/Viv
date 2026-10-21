using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viv.Echo.Grpc;
using Viv.ServiceProxy.Protos;

namespace Viv.ServiceProxy.Apex
{
    /// <summary>
    /// 注册查询 Apex 客户端应用的 gRPC 访问。
    ///
    /// 查询是 Scoped：gRPC 客户端的拦截器要读当前请求的 IVivContext，单例会把那份上下文捕获住。
    /// 缓存单独做单例，多条连接之间共用。
    /// </summary>
    public static class ApexClientAppServiceCollectionExtensions
    {
        public static IServiceCollection AddApexClientAppQuery(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(nameof(ApexClientAppOption));
            var option = section.Get<ApexClientAppOption>() ?? new ApexClientAppOption();

            services.Configure<ApexClientAppOption>(section);

            services.AddVivGrpcClient<ApexAppQuery.ApexAppQueryClient>(
                option.ServiceName, option.UseServiceDiscovery);

            services.TryAddSingleton<ClientAppCheckCache>();
            services.TryAddScoped<IClientAppQuery, ApexClientAppQuery>();

            return services;
        }
    }
}
