using Microsoft.Extensions.Http;

namespace Viv.Aspire.Gateway.Magic
{
    public static class GatewayExtensions
    {
        public static IServiceCollection AddAllHttpClientsIgnoreSslErrors(this IServiceCollection services)
        {
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, IgnoreSslErrorsFilter>();
            return services;
        }

        private class IgnoreSslErrorsFilter : IHttpMessageHandlerBuilderFilter
        {
            public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
            {
                return (builder) =>
                {
                    next(builder);

                    if (builder.PrimaryHandler is HttpClientHandler handler)
                    {
                        // 开发环境：信任所有证书
                        handler.ServerCertificateCustomValidationCallback = (a, b, c, d) => true;
                    }
                };
            }
        }

    }
}
