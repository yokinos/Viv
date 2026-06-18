using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Reflection;
using System.Text;
using Viv.Engine.Enums;
using Viv.Engine.Filter;
using Viv.Engine.Interface;

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
                // 心跳间隔（保持连接）
                KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

            app.UseWebSockets(webSocketOptions);

            app.UseWebSockets();
            app.MapSockers("/line", services.GetService<IWebSocketHandler>());
            return app;
        }
        public static void MapSockers(this IApplicationBuilder application, PathString path, IWebSocketHandler handler)
        {
            //return application.Map(path, (x) => x.UseMiddleware<SocketMiddleware>(handler));
        }

        public static IServiceCollection AddWebSocketService(this IServiceCollection services)
        {
            Type baseType = typeof(IWebSocketHandler);
            foreach (var type in baseType.Assembly.GetTypes())
            {
                if (type.GetTypeInfo().BaseType == baseType)
                {
                    services.AddSingleton(type);
                }
            }
            return services;
        }

        /// <summary>
        /// 注册Swagger（带JWT授权）
        /// </summary>
        public static void AddSwagger(this IServiceCollection services, OpenApiInfo openApiInfo)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", openApiInfo);

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "请输入 Token，无需加 Bearer"
                });

                var reference = new OpenApiSecuritySchemeReference("Bearer")
                {
                    Reference = new OpenApiReferenceWithDescription()
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                };

                var requirement = new OpenApiSecurityRequirement
                {
                    { reference, new List<string>() { } }
                };

                c.AddSecurityRequirement(a => requirement);

                // 启用XML注释（可选）
                var xmlFile = $"{typeof(WebApiExtensions).Assembly.GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath, true);
                }
            });
        }

        /// <summary>
        /// 启用SwaggerUI
        /// </summary>
        public static void VivUseSwagger(this WebApplication app, VivEnv env)
        {
            if (env != VivEnv.Production)
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Viv API v1.0");
                    c.RoutePrefix = "swagger";
                    c.EnableFilter();
                    c.DocExpansion(DocExpansion.List);
                    c.DisplayRequestDuration();
                    c.EnableDeepLinking();
                });
            }
        }
    }
}