using System.Reflection;
using Viv.Delusion.Extension;

namespace Viv.Delusion.Magic
{
    /// <summary>
    /// 类型扫描工具 - 支持按过滤条件扫描，或按基类型/接口/Attribute 扫描
    /// </summary>
    public static class TypeScanMagic
    {
        #region 基础过滤扫描

        /// <summary>
        /// 按 FilterTypeOptions 指定的程序集、命名空间、基类型、命名规则、Attribute 扫描
        /// </summary>
        public static List<Type> Scan(FilterTypeOptions filter)
        {
            ArgumentNullException.ThrowIfNull(filter);

            if (string.IsNullOrWhiteSpace(filter.AssemblyName))
                throw new ArgumentException("程序集名称不能为空", nameof(filter));

            var assembly = Assembly.Load(filter.AssemblyName);
            var types = GetLoadableTypes(assembly);

            var result = new List<Type>();

            foreach (var type in types)
            {
                if (!IsCandidateClass(type))
                    continue;

                if (!MatchNamespace(type, filter.Namespace))
                    continue;

                if (!MatchNamePattern(type, filter))
                    continue;

                if (filter.BaseType != null && !MatchBaseType(type, filter.BaseType))
                    continue;

                if (filter.AttributeType != null && !HasAttribute(type, filter.AttributeType))
                    continue;

                result.Add(type);
            }

            return result.Distinct().ToList();
        }

        /// <summary>
        /// 批量扫描多个过滤条件
        /// </summary>
        public static List<Type> ScanRange(IEnumerable<FilterTypeOptions> filters)
        {
            ArgumentNullException.ThrowIfNull(filters);

            var result = new List<Type>();

            foreach (var filter in filters)
            {
                if (filter == null)
                    continue;

                result.AddRange(Scan(filter));
            }

            return result.Distinct().ToList();
        }

        #endregion

        #region 基类型 / 接口扫描

        /// <summary>
        /// 扫描所有已加载程序集中实现了 TBase 的非抽象类
        /// </summary>
        public static List<Type> ScanTypes<TBase>(Func<Type, bool>? matchPredicate = null)
            => ScanTypes(typeof(TBase), matchPredicate);

        /// <summary>
        /// 扫描指定程序集中实现了 TBase 的非抽象类
        /// </summary>
        public static List<Type> ScanTypes<TBase>(IEnumerable<string> assemblyNames, Func<Type, bool>? matchPredicate = null)
            => ScanTypes(typeof(TBase), assemblyNames, matchPredicate);

        /// <summary>
        /// 扫描所有已加载程序集中实现了 targetType 的非抽象类
        /// </summary>
        public static List<Type> ScanTypes(Type targetType, Func<Type, bool>? matchPredicate = null)
        {
            ArgumentNullException.ThrowIfNull(targetType);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .ToList();

            return ScanTypes(targetType, assemblies, matchPredicate);
        }

        /// <summary>
        /// 扫描指定程序集名称中的实现了 targetType 的非抽象类
        /// </summary>
        public static List<Type> ScanTypes(Type targetType, IEnumerable<string> assemblyNames, Func<Type, bool>? matchPredicate = null)
        {
            ArgumentNullException.ThrowIfNull(targetType);
            ArgumentNullException.ThrowIfNull(assemblyNames);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(a =>
                {
                    var name = a.GetName().Name;
                    return !string.IsNullOrWhiteSpace(name)
                           && assemblyNames.Any(x =>
                               string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            return ScanTypes(targetType, assemblies, matchPredicate);
        }

        /// <summary>
        /// 扫描指定程序集对象中实现了 targetType 的非抽象类
        /// </summary>
        public static List<Type> ScanTypes(Type targetType, IEnumerable<Assembly> assemblies, Func<Type, bool>? matchPredicate = null)
        {
            ArgumentNullException.ThrowIfNull(targetType);
            ArgumentNullException.ThrowIfNull(assemblies);

            return ScanInternal(assemblies, type =>
            {
                if (type == targetType)
                    return false;

                // 与 MatchBaseType 共用统一判定：闭合基类走 IsAssignableFrom，开放泛型走基类链/接口的 GetGenericTypeDefinition 匹配
                if (!IsMatchType(type, targetType))
                    return false;

                if (matchPredicate != null && !matchPredicate(type))
                    return false;

                return true;
            });
        }

        /// <summary>
        /// 扫描所有已加载程序集中实现了指定接口的非抽象类
        /// </summary>
        public static List<Type> ScanByInterface<TInterface>() where TInterface : class
            => ScanTypes<TInterface>();

        /// <summary>
        /// 扫描指定程序集名称中实现了指定接口的非抽象类
        /// </summary>
        public static List<Type> ScanByInterface<TInterface>(IEnumerable<string> assemblyNames, Func<Type, bool>? matchPredicate = null)
            where TInterface : class
            => ScanTypes<TInterface>(assemblyNames, matchPredicate);

        /// <summary>
        /// 扫描所有已加载程序集中实现了指定接口的非抽象类
        /// </summary>
        public static List<Type> ScanByInterface(Type interfaceType, Func<Type, bool>? matchPredicate = null)
            => ScanTypes(interfaceType, matchPredicate);

        /// <summary>
        /// 扫描指定程序集名称中实现了指定接口的非抽象类
        /// </summary>
        public static List<Type> ScanByInterface(Type interfaceType, IEnumerable<string> assemblyNames, Func<Type, bool>? matchPredicate = null)
            => ScanTypes(interfaceType, assemblyNames, matchPredicate);

        #endregion

        #region Attribute 扫描

        /// <summary>
        /// 扫描所有已加载程序集中带有指定 Attribute 的类型
        /// </summary>
        public static List<Type> ScanByAttribute<TAttribute>() where TAttribute : Attribute
            => ScanByAttribute(typeof(TAttribute));

        /// <summary>
        /// 扫描指定程序集中的类型，并要求带有指定 Attribute
        /// </summary>
        public static List<Type> ScanByAttribute<TAttribute>(string assemblyName) where TAttribute : Attribute
            => ScanByAttribute(typeof(TAttribute), assemblyName);

        /// <summary>
        /// 扫描所有已加载程序集中带有指定 Attribute 的类型
        /// </summary>
        private static List<Type> ScanByAttribute(Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(attributeType);

            if (!typeof(Attribute).IsAssignableFrom(attributeType))
                throw new ArgumentException($"{attributeType.FullName} 不是 Attribute 类型", nameof(attributeType));

            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .ToList();

            return ScanByAttributeInternal(attributeType, assemblies);
        }

        /// <summary>
        /// 扫描指定程序集名称中带有指定 Attribute 的类型
        /// </summary>
        public static List<Type> ScanByAttribute(Type attributeType, string assemblyName)
        {
            ArgumentNullException.ThrowIfNull(attributeType);

            if (!typeof(Attribute).IsAssignableFrom(attributeType))
                throw new ArgumentException($"{attributeType.FullName} 不是 Attribute 类型", nameof(attributeType));

            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new ArgumentException("程序集名称不能为空", nameof(assemblyName));

            var assembly = Assembly.Load(assemblyName);
            return ScanByAttributeInternal(attributeType, new List<Assembly> { assembly });
        }

        /// <summary>
        /// 扫描所有已加载程序集中带有指定 Attribute 的类型，并附加过滤条件
        /// </summary>
        public static List<Type> Scan<TAttribute>(FilterTypeOptions filter) where TAttribute : Attribute
        {
            ArgumentNullException.ThrowIfNull(filter);

            var copy = new FilterTypeOptions
            {
                AssemblyName = filter.AssemblyName,
                Namespace = filter.Namespace,
                BaseType = filter.BaseType,
                AttributeType = typeof(TAttribute),
                ClassNameStartsWith = filter.ClassNameStartsWith,
                ClassNameEndsWith = filter.ClassNameEndsWith
            };

            return Scan(copy);
        }

        /// <summary>
        /// 扫描所有已加载程序集中带有指定 Attribute 的类型
        /// </summary>
        public static List<Type> Scan<TAttribute>() where TAttribute : Attribute
        {
            return ScanByAttribute<TAttribute>();
        }

        /// <summary>
        /// 扫描指定程序集中的类型，并要求带有指定 Attribute
        /// </summary>
        public static List<Type> Scan<TAttribute>(string assemblyName) where TAttribute : Attribute
        {
            return ScanByAttribute<TAttribute>(assemblyName);
        }

        #endregion

        #region 内部实现

        private static List<Type> ScanInternal(IEnumerable<Assembly> assemblies, Func<Type, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(assemblies);
            ArgumentNullException.ThrowIfNull(predicate);

            var result = new List<Type>();

            foreach (var assembly in assemblies)
            {
                var types = GetLoadableTypes(assembly);

                var filtered = types.Where(type =>
                    type != null &&
                    IsCandidateClass(type) &&
                    predicate(type));

                result.AddRange(filtered);
            }

            return result.Distinct().ToList();
        }

        private static List<Type> ScanByAttributeInternal(Type attributeType, IEnumerable<Assembly> assemblies)
        {
            ArgumentNullException.ThrowIfNull(attributeType);
            ArgumentNullException.ThrowIfNull(assemblies);

            var result = new List<Type>();

            foreach (var assembly in assemblies)
            {
                var types = GetLoadableTypes(assembly);

                var filtered = types.Where(type =>
                    type != null &&
                    IsCandidateClass(type) &&
                    HasAttribute(type, attributeType));

                result.AddRange(filtered);
            }

            return result.Distinct().ToList();
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
                return [];

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (ex.Types == null)
                    return [];

                return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
        }

        /// <summary>
        /// 是否为候选类
        /// </summary>
        private static bool IsCandidateClass(Type type)
        {
            return type.IsClass && !type.IsAbstract && !type.IsInterface;
        }

        /// <summary>
        /// 命名空间匹配
        /// </summary>
        private static bool MatchNamespace(Type type, string? namespacePrefix)
        {
            if (string.IsNullOrWhiteSpace(namespacePrefix))
                return true;

            return !string.IsNullOrWhiteSpace(type.Namespace)
                   && type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 类名前后缀匹配
        /// </summary>
        private static bool MatchNamePattern(Type type, FilterTypeOptions filter)
        {
            if (!string.IsNullOrWhiteSpace(filter.ClassNameStartsWith) &&
                !type.Name.StartsWith(filter.ClassNameStartsWith, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filter.ClassNameEndsWith) &&
                !type.Name.EndsWith(filter.ClassNameEndsWith, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 基类型匹配（统一闭合/开放泛型判定）
        /// </summary>
        private static bool MatchBaseType(Type type, Type baseType)
        {
            return IsMatchType(type, baseType);
        }

        /// <summary>
        /// 判断 type 是否匹配 baseType（Scan 与 ScanTypes 两套入口共用，避免语义分叉）：
        /// - 闭合 baseType：baseType.IsAssignableFrom(type)（涵盖基类链与接口继承）；
        /// - 开放泛型 baseType（如 typeof(VivConsumer&lt;&gt;)/typeof(IRepository&lt;&gt;)）：type 的基类链或接口中，
        ///   存在以 baseType 为开放泛型定义的闭合泛型即匹配。基类链也要查——VivConsumer&lt;T&gt; 是基类而非接口。
        /// </summary>
        private static bool IsMatchType(Type type, Type baseType)
        {
            if (baseType == null)
                return false;

            if (baseType.IsGenericTypeDefinition)
            {
                for (var current = type; current != null; current = current.BaseType)
                {
                    if (current.IsGenericType
                        && !current.IsGenericTypeDefinition
                        && current.GetGenericTypeDefinition() == baseType)
                    {
                        return true;
                    }
                }

                return type.GetInterfaces()
                    .Any(i => i.IsGenericType &&
                              i.GetGenericTypeDefinition() == baseType);
            }

            return baseType.IsAssignableFrom(type);
        }

        /// <summary>
        /// 是否包含指定 Attribute
        /// </summary>
        private static bool HasAttribute(Type type, Type attributeType)
        {
            if (attributeType == null)
                return false;

            if (!typeof(Attribute).IsAssignableFrom(attributeType))
                throw new ArgumentException($"{attributeType.FullName} 不是 Attribute 类型", nameof(attributeType));

            return type.GetCustomAttributes(attributeType, inherit: false).Length != 0;
        }

        #endregion
    }

    /// <summary>
    /// 类型扫描过滤条件
    /// </summary>
    public class FilterTypeOptions
    {
        /// <summary>
        /// 要扫描的程序集名称（Assembly.Load 参数）
        /// </summary>
        public string AssemblyName { get; set; } = string.Empty;

        /// <summary>
        /// 类型所在命名空间（可选，前缀匹配）
        /// </summary>
        public string Namespace { get; set; } = string.Empty;

        /// <summary>
        /// 目标基类型、接口或开放泛型定义（可选）
        /// </summary>
        public Type? BaseType { get; set; }

        /// <summary>
        /// 目标 Attribute 类型（可选）
        /// </summary>
        public Type? AttributeType { get; set; }

        /// <summary>
        /// 类型名称以此开始（可选）
        /// </summary>
        public string ClassNameStartsWith { get; set; } = string.Empty;

        /// <summary>
        /// 类型名称以此结束（可选）
        /// </summary>
        public string ClassNameEndsWith { get; set; } = string.Empty;
    }
}
