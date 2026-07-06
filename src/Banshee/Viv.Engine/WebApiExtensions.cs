using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;
using System.Reflection;
using Viv.Contracts.Interface;

namespace Viv.Engine
{
    public static class WebApiExtensions
    {
        /// <summary>
        /// 启用 WebSocket 服务
        /// </summary>
        public static IApplicationBuilder UseVivWebSocket(this IApplicationBuilder app, IServiceProvider services)
        {
            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            app.UseWebSockets(webSocketOptions);
            app.MapSockers("/line", services.GetService<IWebSocketHandler>());
            return app;
        }

        public static void MapSockers(this IApplicationBuilder application, PathString path, IWebSocketHandler handler)
        {
        }

        public static IServiceCollection AddWebSocketService(this IServiceCollection services)
        {
            Type baseType = typeof(IWebSocketHandler);
            foreach (var type in baseType.Assembly.GetTypes())
            {
                if (type.GetTypeInfo().BaseType == baseType)
                    services.AddSingleton(type);
            }
            return services;
        }

        /// <summary>
        /// 启用 Scalar API 文档（仅在非生产环境）
        /// </summary>
        public static void VivUseScalar(this WebApplication app, string title)
        {
            app.MapScalarApiReference(options =>
            {
                options.Title = title;
                options.Theme = ScalarTheme.Purple;
                options.Authentication = new ScalarAuthenticationOptions
                {
                    PreferredSecuritySchemes = ["Bearer"]
                };
            });
        }
    }
}
