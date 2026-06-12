using System.Reflection;
using Viv.Delusion.Extension;

namespace Viv.Delusion.Magic
{
    /// <summary>
    /// 类型扫描工具 — 按 FilterTypeOptions 精确控制，或按基类型/接口全域扫描
    /// </summary>
    public static class TypeScanMagic
    {
        /// <summary>
        /// 按 <see cref="FilterTypeOptions"/> 指定的程序集、命名空间、基类型、命名规则扫描
        /// </summary>
        /// <param name="filter">扫描条件（AssemblyName 必填）</param>
        /// <returns>符合条件的非抽象类列表</returns>
        public static List<Type> Scan(FilterTypeOptions filter)
        {
            var result = new List<Type>();
            var assembly = Assembly.Load(filter.AssemblyName);
            var types = assembly.GetExportedTypes();

            foreach (var type in types)
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                if (!MatchNamespace(type, filter.NameSpace))
                    continue;

                if (!MatchNamePattern(type, filter))
                    continue;

                if (filter.BaseType != null && !MatchBaseType(type, filter.BaseType))
                    continue;

                result.Add(type);
            }

            return result;
        }

        /// <summary>
        /// 批量扫描多个过滤条件（去重由调用方自行处理）
        /// </summary>
        public static List<Type> ScanRange(List<FilterTypeOptions> filters)
        {
            var result = new List<Type>();
            foreach (var filter in filters)
                result.AddRange(Scan(filter));
            return result;
        }

        /// <summary>
        /// 扫描所有已加载程序集中实现了 <typeparamref name="TBase"/> 的非抽象类
        /// </summary>
        /// <typeparam name="TBase">目标基类型或接口</typeparam>
        /// <param name="matchPredicate">额外筛选条件（可选）</param>
        public static List<Type> ScanTypes<TBase>(Func<Type, bool>? matchPredicate = null)
            => ScanTypes(typeof(TBase), matchPredicate);

        /// <summary>
        /// 扫描指定程序集中实现了 <typeparamref name="TBase"/> 的非抽象类
        /// </summary>
        /// <typeparam name="TBase">目标基类型或接口</typeparam>
        /// <param name="assemblyNames">目标程序集名称（匹配 FullName 前缀）</param>
        /// <param name="matchPredicate">额外筛选条件（可选）</param>
        public static List<Type> ScanTypes<TBase>(List<string> assemblyNames, Func<Type, bool>? matchPredicate = null)
            => ScanTypes(typeof(TBase), assemblyNames, matchPredicate);

        /// <summary>
        /// 扫描所有已加载程序集中实现了 <paramref name="targetType"/> 的非抽象类
        /// </summary>
        /// <param name="targetType">目标基类型或接口</param>
        /// <param name="matchPredicate">额外筛选条件（可选）</param>
        /// <exception cref="ArgumentNullException">targetType 为 null 时抛出</exception>
        public static List<Type> ScanTypes(Type targetType, Func<Type, bool>? matchPredicate = null)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.FullName))
                .ToList();
            return ScanTypes(targetType, assemblies, matchPredicate);
        }

        /// <summary>
        /// 扫描指定程序集中实现了 <paramref name="targetType"/> 的非抽象类
        /// </summary>
        /// <param name="targetType">目标基类型或接口</param>
        /// <param name="assemblyNames">目标程序集名称（匹配 FullName 前缀）</param>
        /// <param name="matchPredicate">额外筛选条件（可选）</param>
        /// <exception cref="ArgumentException">assemblyNames 为 null 或空时抛出</exception>
        public static List<Type> ScanTypes(Type targetType, List<string> assemblyNames, Func<Type, bool>? matchPredicate = null)
        {
            if (assemblyNames == null || !assemblyNames.Any())
                throw new ArgumentException("程序集名称列表不能为空", nameof(assemblyNames));

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic
                         && !string.IsNullOrEmpty(a.FullName)
                         && assemblyNames.Any(name => a.FullName.StartsWith($"{name},")))
                .ToList();

            return ScanTypes(targetType, assemblies, matchPredicate);
        }

        /// <summary>
        /// 核心扫描 — 遍历指定程序集，找出所有非抽象、实现了 targetType 且满足额外条件的类
        /// </summary>
        private static List<Type> ScanTypes(Type targetType, List<Assembly> assemblies, Func<Type, bool>? matchPredicate = null)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));
            if (!assemblies.Any())
                return [];

            var result = new List<Type>();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    var filtered = types.Where(t =>
                        t is { IsAbstract: false, IsInterface: false }
                        && targetType.IsAssignableFrom(t)
                        && t != targetType);

                    if (matchPredicate != null)
                        filtered = filtered.Where(matchPredicate);

                    result.AddRange(filtered);
                }
                catch (ReflectionTypeLoadException) { }
            }

            return result;
        }

        /// <summary>
        /// 命名空间匹配：未配置命名空间视为全通过
        /// </summary>
        private static bool MatchNamespace(Type type, string? nameSpace)
        {
            if (string.IsNullOrEmpty(nameSpace))
                return true;
            return !type.Namespace.IsNullOrEmpty() && type.Namespace!.StartsWith(nameSpace);
        }

        /// <summary>
        /// 类名前缀 / 后缀匹配
        /// </summary>
        private static bool MatchNamePattern(Type type, FilterTypeOptions filter)
        {
            if (!filter.ClassNameStart.IsNullOrEmpty() && !type.Name.StartsWith(filter.ClassNameStart))
                return false;
            if (!filter.ClassNameEndWith.IsNullOrEmpty() && !type.Name.EndsWith(filter.ClassNameEndWith))
                return false;
            return true;
        }

        /// <summary>
        /// 基类型匹配：普通类型用 IsAssignableFrom，开放泛型用接口泛型定义比对
        /// </summary>
        private static bool MatchBaseType(Type type, Type baseType)
        {
            if (baseType.IsGenericTypeDefinition)
            {
                return type.GetInterfaces()
                    .Any(i => i.IsGenericType
                           && i.GetGenericTypeDefinition() == baseType);
            }

            return baseType.IsAssignableFrom(type);
        }
    }

    /// <summary>
    /// 类型扫描过滤条件
    /// </summary>
    public class FilterTypeOptions
    {
        /// <summary>
        /// 要扫描的程序集名称（必填，Assembly.Load 的参数）
        /// </summary>
        public string AssemblyName { get; set; } = string.Empty;

        /// <summary>
        /// 类型所在命名空间（可选，匹配前缀）
        /// </summary>
        public string NameSpace { get; set; } = string.Empty;

        /// <summary>
        /// 目标基类型、接口或开放泛型定义（可选）
        /// </summary>
        public Type? BaseType { get; set; }

        /// <summary>
        /// 类型名称以此结束（可选）
        /// </summary>
        public string ClassNameEndWith { get; set; } = string.Empty;

        /// <summary>
        /// 类型名称以此开始（可选）
        /// </summary>
        public string ClassNameStart { get; set; } = string.Empty;
    }
}
