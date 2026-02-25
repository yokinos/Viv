using Autofac;
using Autofac.Core.Registration;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System;

namespace Viv.Aoi
{
    /// <summary>
    /// Viv框架内置的Autofac服务定位器（适配.NET 10）
    /// 核心职责：从Autofac容器中解析/获取已注册的服务，无需构造函数注入
    /// </summary>
    /// <remarks>
    /// 支持的生命周期：
    /// 1. 单例（SingleInstance）：全局唯一实例；
    /// 2. 作用域（InstancePerLifetimeScope）：基于当前请求/临时作用域；
    /// 3. 瞬时（InstancePerDependency）：每次解析新实例。
    /// 注意：非必要场景不建议使用服务定位器（违背依赖注入原则），仅用于无法构造注入的场景（如静态类、第三方组件）
    /// </remarks>
    public static class VivLocator
    {
        /// <summary>
        /// Autofac根容器（应用全局唯一，初始化后不可修改）
        /// </summary>
        private static IContainer? _rootContainer;

        /// <summary>
        /// 标记是否已初始化容器
        /// </summary>
        private static bool _isInitialized;

        /// <summary>
        /// Web场景下的HttpContext访问器（用于获取当前请求作用域）
        /// </summary>
        private static IHttpContextAccessor? _httpContextAccessor;

        /// <summary>
        /// 初始化Autofac服务定位器（非Web场景）
        /// </summary>
        /// <param name="container">Autofac根容器（由Program.cs构建）</param>
        /// <exception cref="ArgumentNullException">容器为空时抛出</exception>
        /// <exception cref="InvalidOperationException">重复初始化时抛出</exception>
        public static void Initialize(IContainer container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container), "Autofac根容器不能为空！");
            }

            if (_isInitialized)
            {
                throw new InvalidOperationException("VivLocator已初始化，禁止重复调用！");
            }

            _rootContainer = container;
            _isInitialized = true;
        }

        /// <summary>
        /// 初始化Autofac服务定位器（Web场景专用，关联HttpContext）
        /// </summary>
        /// <param name="container">Autofac根容器</param>
        /// <param name="httpContextAccessor">HttpContext访问器（需提前注册到DI）</param>
        public static void Initialize(IContainer container, IHttpContextAccessor httpContextAccessor)
        {
            Initialize(container);
            _httpContextAccessor = httpContextAccessor ??
                throw new ArgumentNullException(nameof(httpContextAccessor), "Web场景必须提供HttpContextAccessor！");
        }

        /// <summary>
        /// 解析单例/瞬时服务（Autofac：SingleInstance/InstancePerDependency）
        /// </summary>
        /// <typeparam name="T">要解析的服务接口类型</typeparam>
        /// <returns>服务实例</returns>
        /// <exception cref="InvalidOperationException">未初始化/解析失败时抛出</exception>
        public static T GetService<T>() where T : notnull
        {
            // 1. 校验初始化状态
            if (!_isInitialized)
            {
                throw new InvalidOperationException("请先调用VivLocator.Initialize()初始化Autofac容器！");
            }

            // 2. 从根容器解析服务（单例/瞬时服务适合从根容器解析）
            try
            {
                var service = _rootContainer.Resolve<T>();
                return service;
            }
            catch (ComponentNotRegisteredException ex)
            {
                throw new InvalidOperationException($"服务类型 {typeof(T).FullName} 未在Autofac容器中注册！", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"解析服务 {typeof(T).FullName} 失败：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解析作用域服务（Autofac：InstancePerLifetimeScope）
        /// </summary>
        /// <typeparam name="T">要解析的服务接口类型</typeparam>
        /// <returns>当前作用域的服务实例</returns>
        /// <exception cref="InvalidOperationException">未初始化时抛出</exception>
        public static T GetScopedService<T>() where T : notnull
        {
            // 基础校验
            if (!_isInitialized)
            {
                throw new InvalidOperationException("请先调用VivLocator.Initialize()初始化Autofac容器！");
            }

            ILifetimeScope? currentScope = null;

            // Web场景：优先从HttpContext获取当前请求作用域
            if (_httpContextAccessor != null)
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    // 从HttpContext获取Autofac作用域
                    currentScope = httpContext.RequestServices.GetAutofacRoot();
                }
            }

            // 3. 非Web场景/无请求上下文：创建临时作用域
            if (currentScope == null)
            {
                currentScope = _rootContainer.BeginLifetimeScope();
                // 提示：非Web场景建议通过CreateTempScope()手动管理作用域生命周期
            }

            // 4. 解析作用域服务
            try
            {
                var service = currentScope.Resolve<T>();
                return service;
            }
            catch (ComponentNotRegisteredException ex)
            {
                throw new InvalidOperationException($"作用域服务类型 {typeof(T).FullName} 未在Autofac容器中注册！", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"解析作用域服务 {typeof(T).FullName} 失败：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 手动创建临时作用域（非Web场景推荐使用）
        /// </summary>
        /// <returns>Autofac作用域实例</returns>
        /// <exception cref="InvalidOperationException">未初始化时抛出</exception>
        public static ILifetimeScope CreateTempScope()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("请先调用VivLocator.Initialize()初始化Autofac容器！");
            }
            return _rootContainer.BeginLifetimeScope();
        }

        /// <summary>
        /// 释放根容器
        /// </summary>
        public static void Dispose()
        {
            if (_isInitialized && _rootContainer != null)
            {
                _rootContainer.Dispose();
                _rootContainer = null;
                _isInitialized = false;
                _httpContextAccessor = null;
            }
        }
    }
}