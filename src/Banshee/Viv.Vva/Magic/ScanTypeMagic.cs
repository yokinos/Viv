using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Viv.Vva.Magic
{
    /// <summary>
    /// 通用类型扫描工具类
    /// 支持扫描实现指定类型的类，并通过委托自定义匹配规则
    /// </summary>
    public class ScanTypeMagic
    {
        /// <summary>
        /// 扫描所有程序集中实现了指定类型的类
        /// </summary>
        /// <typeparam name="T">目标基类型/接口</typeparam>
        /// <param name="matchPredicate">自定义匹配委托（可选），用于额外的类型筛选（如按名称匹配）</param>
        /// <returns>符合条件的类型列表</returns>
        public List<Type> ScanTypes<T>(Func<Type, bool>? matchPredicate = null)
        {
            return ScanTypes(typeof(T), matchPredicate);
        }

        /// <summary>
        /// 扫描指定程序集中实现了指定类型的类
        /// </summary>
        /// <typeparam name="T">目标基类型/接口</typeparam>
        /// <param name="assemblyNames">要扫描的程序集名称列表</param>
        /// <param name="matchPredicate">自定义匹配委托（可选），用于额外的类型筛选（如按名称匹配）</param>
        /// <returns>符合条件的类型列表</returns>
        public List<Type> ScanTypes<T>(List<string> assemblyNames, Func<Type, bool> matchPredicate = null)
        {
            return ScanTypes(typeof(T), assemblyNames, matchPredicate);
        }

        /// <summary>
        /// 扫描所有程序集中实现了指定类型的类（非泛型版本）
        /// </summary>
        /// <param name="targetType">目标基类型/接口</param>
        /// <param name="matchPredicate">自定义匹配委托（可选），用于额外的类型筛选（如按名称匹配）</param>
        /// <returns>符合条件的类型列表</returns>
        public List<Type> ScanTypes(Type targetType, Func<Type, bool>? matchPredicate = null)
        {
            // 获取当前应用域中所有非动态程序集
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.FullName)).ToList();
            return ScanTypes(targetType, assemblies, matchPredicate);
        }

        /// <summary>
        /// 扫描指定程序集中实现了指定类型的类（非泛型版本）
        /// </summary>
        /// <param name="targetType">目标基类型/接口</param>
        /// <param name="assemblyNames">要扫描的程序集名称列表</param>
        /// <param name="matchPredicate">自定义匹配委托（可选），用于额外的类型筛选（如按名称匹配）</param>
        /// <returns>符合条件的类型列表</returns>
        public List<Type> ScanTypes(Type targetType, List<string> assemblyNames, Func<Type, bool> matchPredicate = null)
        {
            if (assemblyNames == null || !assemblyNames.Any())
            {
                throw new ArgumentException("程序集名称列表不能为空", nameof(assemblyNames));
            }

            // 筛选出指定名称的程序集
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.FullName) &&
                            assemblyNames.Any(name => a.FullName.StartsWith($"{name},")))
                .ToList();

            return ScanTypes(targetType, assemblies, matchPredicate);
        }

        /// <summary>
        /// 核心扫描方法
        /// </summary>
        /// <param name="targetType">目标基类型/接口</param>
        /// <param name="assemblies">要扫描的程序集列表</param>
        /// <param name="matchPredicate">自定义匹配委托（可选）</param>
        /// <returns>符合条件的类型列表</returns>
        private List<Type> ScanTypes(Type targetType, List<Assembly> assemblies, Func<Type, bool>? matchPredicate = null)
        {
            var result = new List<Type>();

            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType), "目标类型不能为空");
            }

            if (assemblies == null || !assemblies.Any())
            {
                Console.WriteLine("没有可扫描的程序集");
                return result;
            }

            foreach (var assembly in assemblies)
            {
                try
                {
                    // 获取程序集中所有类型
                    var types = assembly.GetTypes();

                    // 基础筛选条件：
                    // 1. 不是抽象类
                    // 2. 不是接口
                    // 3. 实现/继承了目标类型
                    // 4. 不是目标类型本身
                    var baseFilteredTypes = types.Where(t =>
                        !t.IsAbstract &&
                        !t.IsInterface &&
                        targetType.IsAssignableFrom(t) &&
                        t != targetType);

                    // 如果有自定义匹配委托，应用额外筛选
                    var finalFilteredTypes = matchPredicate != null
                        ? baseFilteredTypes.Where(matchPredicate)
                        : baseFilteredTypes;

                    result.AddRange(finalFilteredTypes);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 处理程序集类型加载异常
                    Console.WriteLine($"扫描程序集 {assembly.FullName} 时发生类型加载错误: {ex.Message}");
                    // 输出加载失败的类型信息
                    if (ex.LoaderExceptions != null)
                    {
                        foreach (var loaderEx in ex.LoaderExceptions)
                        {
                            Console.WriteLine($"  加载错误详情: {loaderEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"扫描程序集 {assembly.FullName} 时发生错误: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 便捷方法：扫描实现指定类型且类型名称符合规则的类
        /// </summary>
        /// <typeparam name="T">目标基类型/接口</typeparam>
        /// <param name="nameContains">类型名称需要包含的字符串</param>
        /// <param name="assemblyNames">要扫描的程序集名称列表（可选）</param>
        /// <returns>符合条件的类型列表</returns>
        public List<Type> ScanTypesByName<T>(string nameContains, List<string> assemblyNames = null)
        {
            // 构建按名称匹配的委托
            Func<Type, bool> namePredicate = t =>
                t.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase);

            return assemblyNames == null
                ? ScanTypes<T>(namePredicate)
                : ScanTypes<T>(assemblyNames, namePredicate);
        }

        /// <summary>
        /// 执行扫描到的类型的指定方法
        /// </summary>
        /// <param name="types">要执行方法的类型列表</param>
        /// <param name="methodName">要执行的方法名称</param>
        /// <param name="parameters">方法参数（可选）</param>
        /// <returns>方法执行结果列表</returns>
        public List<object> ExecuteMethodOnTypes(List<Type> types, string methodName, params object[] parameters)
        {
            var results = new List<object>();

            if (types == null || !types.Any())
            {
                Console.WriteLine("没有可执行方法的类型");
                return results;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                throw new ArgumentException("方法名称不能为空", nameof(methodName));
            }

            foreach (var type in types)
            {
                try
                {
                    // 创建类型实例（要求有无参构造函数）
                    var instance = Activator.CreateInstance(type);
                    if (instance == null)
                    {
                        Console.WriteLine($"无法实例化类型 {type.FullName}");
                        continue;
                    }

                    // 获取方法信息
                    var method = type.GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (method == null)
                    {
                        Console.WriteLine($"类型 {type.FullName} 中未找到方法 {methodName}");
                        continue;
                    }

                    // 执行方法
                    var result = method.Invoke(instance, parameters);
                    results.Add(result);
                    Console.WriteLine($"成功执行 {type.FullName}.{methodName} 方法");
                }
                catch (MissingMethodException)
                {
                    Console.WriteLine($"类型 {type.FullName} 没有无参构造函数，无法实例化");
                }
                catch (TargetInvocationException ex)
                {
                    Console.WriteLine($"执行 {type.FullName}.{methodName} 方法时发生错误: {ex.InnerException?.Message ?? ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理类型 {type.FullName} 时发生错误: {ex.Message}");
                }
            }

            return results;
        }
    }
}
