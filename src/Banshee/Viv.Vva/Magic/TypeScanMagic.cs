using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Viv.Vva.Extension;

namespace Viv.Vva.Magic
{
    public static class TypeScanMagic
    {
        /// <summary>
        /// 扫描：类 + 命名空间 + 基类/接口/泛型定义
        /// </summary>
        public static List<Type> Scan(FilterTypeOptions filter)
        {
            var result = new List<Type>();

            // 加载程序集
            var assembly = Assembly.Load(filter.AssemblyName);
            var types = assembly.GetExportedTypes();

            foreach (var type in types)
            {
                // 必须是类（排除抽象、静态）
                if (!type.IsClass || type.IsAbstract)
                    continue;

                // 命名空间筛选（配置了才校验）
                if (!string.IsNullOrEmpty(filter.NameSpace) && type.Namespace != filter.NameSpace)
                    continue;

                if (!filter.ClassNameStart.IsNullOrEmpty() && !type.Name.StartsWith(filter.ClassNameStart))
                    continue;

                if (!filter.ClassNameEndWith.IsNullOrEmpty() && !type.Name.EndsWith(filter.ClassNameEndWith))
                    continue;

                // 基类 / 接口 / 泛型定义 筛选
                if (filter.BaseType != null)
                {
                    // 情况1：要匹配 开放泛型（如 IRepository<>）
                    if (filter.BaseType.IsGenericTypeDefinition)
                    {
                        bool match = type.GetInterfaces()
                            .Any(x => x.IsGenericType &&
                                 x.GetGenericTypeDefinition() == filter.BaseType);

                        if (!match) continue;
                    }
                    // 情况2：普通类型/接口
                    else
                    {
                        if (!filter.BaseType.IsAssignableFrom(type))
                            continue;
                    }
                }

                result.Add(type);
            }

            return result;
        }

        /// <summary>
        /// 批量扫描
        /// </summary>
        public static List<Type> ScanRange(List<FilterTypeOptions> filters)
        {
            var result = new List<Type>();
            foreach (var filter in filters)
                result.AddRange(Scan(filter));
            return result;
        }
    }

    /// <summary>
    /// 类型扫描配置
    /// </summary>
    public class FilterTypeOptions
    {
        /// <summary>
        /// 程序集名称
        /// </summary>
        public string AssemblyName { get; set; } = string.Empty;

        /// <summary>
        /// 命名空间
        /// </summary>
        public string NameSpace { get; set; } = string.Empty;

        /// <summary>
        /// 基类、接口、泛型定义
        /// </summary>
        public Type? BaseType { get; set; }

        /// <summary>
        /// 类型名称以什么结尾
        /// </summary>
        public string ClassNameEndWith { get; set; } = string.Empty;

        /// <summary>
        /// 类型名称以什么开始
        /// </summary>
        public string ClassNameStart { get; set; } = string.Empty;
    }
}