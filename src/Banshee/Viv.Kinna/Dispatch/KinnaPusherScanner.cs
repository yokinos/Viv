using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Dispatch
{
    /// <summary>
    /// 按 Hub 找 Pusher：凡是声明了 <see cref="IKinnaPusher{THub, TEvent}"/> 的类就算这个 Hub 的。
    ///
    /// 判定只看接口，不看命名或所在程序集里有几个 Hub —— 一个程序集装多个 Hub 时，
    /// 按「程序集里唯一的 Pusher」这类约定推断必然出错。
    /// </summary>
    internal static class KinnaPusherScanner
    {
        /// <summary>
        /// 扫过就别再扫。装配阶段可能被多个模块各调一次 AddKinnaPusher，
        /// Assembly.GetTypes() 是整程序集反射，没必要重复付这个钱。
        /// </summary>
        private static readonly ConcurrentDictionary<(Assembly Assembly, Type Hub), Type[]> Cache = new();

        /// <param name="extraAssemblies">除 Hub 所在程序集之外还要扫的程序集</param>
        internal static IReadOnlyList<Type> Find<THub>(IReadOnlyList<Assembly> extraAssemblies) where THub : Hub
        {
            var hub = typeof(THub);

            // Hub 所在程序集是默认位置：Pusher 和它服务的 Hub 通常一起发布
            var assemblies = new List<Assembly> { hub.Assembly };
            foreach (var assembly in extraAssemblies)
            {
                if (assembly != null && !assemblies.Contains(assembly))
                    assemblies.Add(assembly);
            }

            var found = new List<Type>();
            foreach (var assembly in assemblies)
            {
                found.AddRange(Cache.GetOrAdd((assembly, hub), static key => Scan(key.Assembly, key.Hub)));
            }

            return found;
        }

        private static Type[] Scan(Assembly assembly, Type hub)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 有类型加载不了时用拿得到的那些继续扫。少一个 Pusher 的后果是解析投递器时
                // 报「没有注册 IKinnaMessageDispatcher<,>」，比在这里抛一堆 FileLoadException 好定位
                types = ex.Types.Where(x => x != null).ToArray()!;
            }

            return types
                .Where(x => x is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
                .Where(x => x.GetInterfaces().Any(contract => IsContractFor(contract, hub)))
                .ToArray();
        }

        private static bool IsContractFor(Type contract, Type hub)
            => contract.IsGenericType
               && contract.GetGenericTypeDefinition() == typeof(IKinnaPusher<,>)
               && contract.GetGenericArguments()[0] == hub;
    }
}
