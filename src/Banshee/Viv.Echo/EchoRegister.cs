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

            if (options.EnableGrpc)
            {
                services.AddTransient<VivGrpcInterceptor>();
            }
        }
    }
}
