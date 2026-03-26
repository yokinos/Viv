using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Viv.Aoi
{
    /// <summary>
    /// Viv 框架全能服务定位器
    /// 同时支持：.NET官方DI + Autofac原生解析
    /// </summary>
    public static class VivLocator
    {
        private static IServiceProvider _serviceProvider = null!;
        private static IHttpContextAccessor _httpContextAccessor = null!;
        private static ILifetimeScope _lifetimeScope = null!;
        private static bool _initialized = false;

        /// <summary>
        /// 统一初始化（只需传入 app.Services）
        /// </summary>
        public static void Initialize(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            if (_initialized)
                throw new InvalidOperationException("请勿重复初始化！");

            _serviceProvider = serviceProvider;
            _lifetimeScope = serviceProvider.GetRequiredService<ILifetimeScope>();
            _httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>() ?? null!;

            _initialized = true;
        }

        /// <summary>
        /// 直接用 Autofac 原生解析（最快、最原生）
        /// </summary>
        public static T GetAutofaService<T>() where T : notnull
        {
            CheckInitialized();
            return _lifetimeScope.Resolve<T>();
        }

        /// <summary>
        /// .NET 官方标准解析（兼容所有容器）
        /// </summary>
        public static T GetService<T>() where T : notnull
        {
            CheckInitialized();
            return _serviceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// 获取请求作用域服务（Web专用）
        /// </summary>
        public static T GetScopedService<T>() where T : notnull
        {
            CheckInitialized();

            if (_httpContextAccessor?.HttpContext != null)
            {
                return _httpContextAccessor.HttpContext.RequestServices.GetRequiredService<T>();
            }

            using var scope = _serviceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        /// <summary>
        /// 创建临时作用域
        /// </summary>
        public static IDisposable CreateScope()
        {
            CheckInitialized();
            return _serviceProvider.CreateScope();
        }

        /// <summary>
        /// 检查初始化状态
        /// </summary>
        private static void CheckInitialized()
        {
            if (!_initialized)
                throw new InvalidOperationException("请先调用 VivLocator.Initialize(IServiceProvider) 完成初始化！");
        }
    }
}