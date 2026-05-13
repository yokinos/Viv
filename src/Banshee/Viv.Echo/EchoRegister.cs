using Microsoft.Extensions.DependencyInjection;
using Viv.Echo.Grpc;
using Viv.Echo.Http;
using Viv.Echo.Options;
using Viv.Vva;

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
            services.AddVivGrpcClients(options);
        }
    }
}
