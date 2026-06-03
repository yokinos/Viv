using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Engine.Conveter;
using Viv.Engine.Filter;
using Viv.Engine.Middleware;
using Viv.Engine.Options;
using Viv.Vva.Extension;
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
            // 自动依赖注入
            AutoDependencyRegister(builder);

            // 自定义的注入
            customSet?.Invoke(builder);

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
        }

        /// <summary>
        /// 自动依赖注入
        /// </summary>
        /// <param name="builder"></param>
        private static void AutoDependencyRegister(ContainerBuilder builder)
        {
            var typeList = TypeScanMagic.ScanTypes<IDependency>();
            if (typeList.IsNullOrEmpty())
                return;

            foreach (var type in typeList)
            {
                var attr = type.GetCustomAttribute<VivDependencyAttribute>(false);
                var lifetime = attr?.Lifetime ?? DependencyLifetime.Scoped;
                var asSelf = attr?.AsSelf ?? false;

                var registration = asSelf ? builder.RegisterType(type).AsSelf() : builder.RegisterType(type).AsImplementedInterfaces();
                registration = lifetime switch
                {
                    DependencyLifetime.Singleton => registration.SingleInstance(),
                    DependencyLifetime.Transient => registration.InstancePerDependency(),
                    _ => registration.InstancePerLifetimeScope()
                };

                registration.PreserveExistingDefaults();
            }
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
