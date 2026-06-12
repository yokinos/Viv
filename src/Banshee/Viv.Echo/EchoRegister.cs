using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion;
using Viv.Echo.Grpc;
using Viv.Echo.Http;
using Viv.Echo.Options;

namespace Viv.Echo
{
    public static class EchoRegister
    {
        public static void Initialize(IServiceCollection services, EchoOptions options)
        {
            VivConfigRegistry.Add(options);

            if (options.EnableHttp)
            {
                services.AddHttpClient();
                services.AddScoped<IVivHttpService, VivHttpClient>();
            }

            services.AddTransient<VivGrpcInterceptor>();

            if (options.GrpcEndpoints.Count > 0)
            {
                // 旧的配置方式 — 兼容过渡期
                services.AddVivGrpcClientsFromConfig(options);
            }
            else
            {
                // 新方式 — Source Generator 自动生成
                services.AddVivGrpcClients();
            }
        }
    }
}
