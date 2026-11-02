using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Contracts.Interface;

namespace Viv.Engine
{
    /// <summary>
    /// 代理形态判定 —— 注册（<see cref="VivEngineExtensions"/>）与两个注册期校验
    /// （<c>UnitOfWorkRegistration</c> / <c>DistributedLockRegistration</c>）共用这一份。
    ///
    /// <para>两条代理路径：</para>
    /// <list type="bullet">
    /// <item><description>有业务接口 → Castle 接口代理（<c>AsImplementedInterfaces</c> +
    /// <c>EnableInterfaceInterceptors</c>），不要求实现方法 virtual。</description></item>
    /// <item><description>没有业务接口 → Castle 类代理（<c>AsSelf</c> + <c>EnableClassInterceptors</c>），
    /// 被拦方法必须是 public virtual，且类型必须 public、非 sealed。</description></item>
    /// </list>
    ///
    /// <para><b>为什么排除 <see cref="IDisposable"/> / <see cref="IAsyncDisposable"/> / <see cref="IDependency"/></b>：
    /// 只实现这几个接口的类没有对外契约，按接口注册只会把它注册成 <c>IDisposable</c> / <c>IDependency</c>，
    /// 按具体类型反而解析不到。它们由 <c>AsSelf</c> 注册才是对的。</para>
    /// </summary>
    internal static class ProxyPolicy
    {
        private static readonly HashSet<Type> NonContractInterfaces =
        [
            typeof(IDisposable),
            typeof(IAsyncDisposable),
            typeof(IDependency)
        ];

        /// <summary>
        /// 该类型是否必须走类代理。仅判「有没有业务接口」，不判能不能代理 ——
        /// sealed / 非 public / 方法非 virtual 由注册期校验用具体的报错挡。
        /// </summary>
        public static bool NeedsClassProxy(Type type) => !HasContractInterface(type);

        /// <summary>
        /// 是否有业务接口（排除纯生命周期 / 标记接口）。
        /// </summary>
        public static bool HasContractInterface(Type type) =>
            type.GetInterfaces().Any(static i => !NonContractInterfaces.Contains(i));
    }
}
