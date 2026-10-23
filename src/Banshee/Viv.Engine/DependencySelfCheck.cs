using System.Reflection;
using Viv.Contracts.Interface;

namespace Viv.Engine
{
    /// <summary>
    /// 启动自检：名字像 Service / Repository 的类，如果既没实现 <see cref="IDependency"/>、
    /// 也没被 DIOption 扫描注册，那它永远不会进容器。真正炸的地方是解析它依赖的接口时，
    /// 现场离原因很远，所以启动时就摆出来。
    /// </summary>
    public static class DependencySelfCheck
    {
        private static readonly string[] NameSuffixes = ["Service", "Repository"];

        /// <summary>
        /// 找出应该进容器却没进的类型。已注册集合取 IDependency 扫描结果 + DIOption 扫描结果
        /// </summary>
        public static List<Type> FindUnregistered(IEnumerable<Type> candidates, IEnumerable<Type> registered)
        {
            var registeredSet = new HashSet<Type>(registered);

            return candidates
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => !t.IsGenericTypeDefinition)
                .Where(t => !typeof(IDependency).IsAssignableFrom(t))
                .Where(t => !registeredSet.Contains(t))
                .Where(t => NameSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 测试项目与测试替身不参与自检：它们里的探针类经常故意不注册
        /// </summary>
        public static bool IsExcludedAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name ?? string.Empty;

            return name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Viv.Fakes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把漏注册的类型拼成一条能直接照做的报错</summary>
        public static string BuildMessage(IReadOnlyList<Type> unregistered)
        {
            const int show = 10;

            var names = string.Join(", ", unregistered.Take(show).Select(t => t.FullName));
            var more = unregistered.Count > show ? $" 等共 {unregistered.Count} 个" : string.Empty;

            return $"""
                这些类名字像服务/仓储，但既没实现 IDependency，也不在 DIOption 的扫描范围内，永远不会被注册：
                {names}{more}
                处理方式：给类实现 Viv.Contracts.Interface.IDependency（需要改生命周期或按 key 注册时加 [VivDependency]），
                或把所在程序集/命名空间配进 appsettings 的 VivOptions.DIOption。
                """;
        }
    }
}
