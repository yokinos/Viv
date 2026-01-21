using System;
using System.ComponentModel;

namespace Viv.Autofac
{
    /// <summary>
    /// Viv框架内置的Autofac服务定位器
    /// 核心职责：从Autofac容器中解析/获取已注册的服务，无需构造函数注入
    /// </summary>
    /// <remarks>
    /// 支持的生命周期：
    /// 1. 单例（SingleInstance）：全局唯一实例；
    /// 2. 作用域（InstancePerLifetimeScope）：基于当前请求/临时作用域；
    /// 3. 瞬时（InstancePerDependency）：每次解析新实例。
    /// </remarks>
    public static class VivLocator
    {
        /// <summary>
        /// Autofac根容器（应用全局唯一，初始化后不可修改）
        /// </summary>
        private static IContainer _rootContainer;

        /// <summary>
        /// 标记是否已初始化容器
        /// </summary>
        private static bool _isInitialized;

        /// <summary>
        /// 初始化Autofac服务定位器
        /// </summary>
        /// <param name="container">Autofac根容器（由Program.cs/Startup.cs构建）</param>
        /// <exception cref="ArgumentNullException">容器为空时抛出</exception>
        /// <exception cref="InvalidOperationException">重复初始化时抛出</exception>
        public static void Initialize(IContainer container)
        {
            // TODO: 后续补充初始化逻辑：校验容器、赋值_rootContainer、标记_isInitialized
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
        /// 解析单例/瞬时服务（Autofac：SingleInstance/InstancePerDependency）
        /// </summary>
        /// <typeparam name="T">要解析的服务接口类型</typeparam>
        /// <returns>服务实例</returns>
        /// <exception cref="InvalidOperationException">未初始化/解析失败时抛出</exception>
        public static T GetService<T>() where T : notnull
        {
            return default;
        }

        /// <summary>
        /// 解析作用域服务（Autofac：InstancePerLifetimeScope/InstancePerRequest）
        /// </summary>
        /// <typeparam name="T">要解析的服务接口类型</typeparam>
        /// <returns>当前作用域的服务实例</returns>
        /// <exception cref="InvalidOperationException">未初始化时抛出</exception>
        public static T GetScopedService<T>() where T : notnull
        {
            // TODO: 后续补充核心逻辑：
            // 1. 校验_isInitialized；
            // 2. Web场景：从HttpContext获取当前请求的Autofac作用域；
            // 3. 非Web场景：创建临时作用域（_rootContainer.BeginLifetimeScope()）；
            // 4. 从作用域中解析服务。
            if (!_isInitialized)
            {
                throw new InvalidOperationException("请先调用VivLocator.Initialize()初始化Autofac容器！");
            }

            // 占位返回：后续替换为实际解析逻辑
            throw new NotImplementedException("作用域服务解析逻辑待实现（需适配Web/非Web场景）");
        }
    }
}