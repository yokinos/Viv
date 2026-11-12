using Microsoft.Extensions.DependencyInjection;
using Viv.Delusion;
using Viv.Keke.Grpc;
using Viv.Keke.Http;
using Viv.Keke.MagicOnion;

namespace Viv.Keke
{
    public static class KekeRegister
    {
        public static void Initialize(IServiceCollection services, KekeOptions options)
        {
            if (options.EnableHttp)
            {
                services.AddHttpClient();
                services.AddScoped<IVivHttpService, VivHttpClient>();
            }

            if (options.GrpcOption != null)
            {
                services.AddTransient<VivGrpcInterceptor>();
            }

            if (options.GrpcOption?.ServesMagicOnion == true)
            {
                services.AddVivMagicOnionServer();
            }
        }
    }
}
