using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Aoi;
using Viv.Engine.Options;
using Viv.Vva.Magic;

namespace Viv.Engine
{
    public static class VivEngineExtensions
    {
        /// <summary>
        /// 注册Viv相关服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static IServiceCollection AddViv(this IServiceCollection services, VivOptions vivOptions)
        {
            ArgumentNullException.ThrowIfNull(vivOptions);
            VivRegister.Register(services, vivOptions);
            return services;
        }

        public static void VivAutofacRegister(this ContainerBuilder builder, DIOptions diOptions, Action<ContainerBuilder> customSet = null)
        {
            // 可能不需要抽象
            if (diOptions == null) return;

            var serviceImplTypes = TypeScanMagic.Scan(diOptions.ServiceImplementation);

            builder.RegisterTypes(serviceImplTypes.ToArray())
                   .AsImplementedInterfaces()
                   .InstancePerLifetimeScope();

            var repoImplTypes = TypeScanMagic.Scan(diOptions.RepositoryImplementation);

            builder.RegisterTypes(repoImplTypes.ToArray())
                   .AsImplementedInterfaces()
                   .InstancePerLifetimeScope();

            customSet?.Invoke(builder);
        }

        /// <summary>
        /// 判断是否为Ajax请求
        /// </summary>
        public static bool IsAjax(this HttpRequest request, string rule = "")
        {
            // 判断是否为Post请求
            bool isPost = request.Method.Equals("Post", StringComparison.OrdinalIgnoreCase);

            // Ajax请求判断
            bool isAjax = request.Headers["X-Requested-With"] == "XMLHttpRequest";

            // 接口路径判断
            bool isApiPath = !string.IsNullOrEmpty(rule) && request.Path.Value.Contains(rule, StringComparison.OrdinalIgnoreCase);

            return isPost || isAjax || isApiPath;
        }

        /// <summary>
        /// 获取请求头中的Token信息
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static string GetJwtToken(this HttpContext context)
        {
            return context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        }
    }
}
