using System;

namespace Viv.Contracts.Enums
{
    /// <summary>
    /// DI 生命周期
    /// </summary>
    public enum DependencyLifetime
    {
        /// <summary>
        /// 每次解析新建实例
        /// </summary>
        Transient = 0,

        /// <summary>
        /// 每个 Scope 单例（默认值，对应 Autofac InstancePerLifetimeScope / MS DI Scoped）
        /// </summary>
        Scoped = 1,

        /// <summary>
        /// 全局单例
        /// </summary>
        Singleton = 2,
    }
}
