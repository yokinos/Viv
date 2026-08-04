using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Viv.Delusion.Magic;
using Viv.Delusion.Mapper;

namespace Viv.Delusion.Extension
{
    public static partial class Extensions
    {
        /// <summary>
        /// [扩展方法] 将DateTime转换为Unix时间戳
        /// </summary>
        /// <param name="self">待转换的DateTime对象（默认基于UTC时间计算）</param>
        /// <param name="isMs">是否返回毫秒级时间戳，默认false（秒级）</param>
        /// <returns>Unix时间戳（秒/毫秒），底层调用ConvertMagic.ToUnixTime实现</returns>
        /// <example>
        /// 示例：new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).ToUnixTime() → 0
        /// 示例：new DateTime(2026,2,11).ToUnixTime(true) → 1749609600000（毫秒级）
        /// </example>
        public static long ToUnixTime(this DateTime self, bool isMs = false)
        {
            return ObjectMapper.ToUnixTime(self, isMs);
        }

        /// <summary>
        /// [扩展方法] 将DateTimeOffset转换为Unix时间戳
        /// </summary>
        /// <param name="self">待转换的DateTimeOffset对象</param>
        /// <param name="isMs">是否返回毫秒级时间戳，默认false（秒级）</param>
        /// <returns>Unix时间戳（秒/毫秒），底层调用ConvertMagic.ToUnixTime实现</returns>
        /// <remarks>DateTimeOffset包含时区信息，转换时会自动换算为UTC时间戳</remarks>
        public static long ToUnixTime(this DateTimeOffset self, bool isMs = false)
        {
            return ObjectMapper.ToUnixTime(self, isMs);
        }

        /// <summary>
        /// [扩展方法] 安全转换对象为指定类型（支持自定义默认值和文化格式）
        /// </summary>
        /// <typeparam name="T">目标转换类型</typeparam>
        /// <param name="obj">待转换的源对象</param>
        /// <param name="defaultvalue">转换失败时的默认值，默认null</param>
        /// <param name="culture">转换使用的文化格式信息，默认使用系统当前文化</param>
        /// <returns>
        /// 1. 转换成功 → 目标类型的对象
        /// 2. 转换失败/源对象为null → 返回defaultvalue
        /// </returns>
        /// <remarks>底层调用ObjectMapper.TryConvert实现，兼容常见类型（数值、字符串、日期等）转换</remarks>
        [return: MaybeNull]
        public static T As<T>(this object? obj, T? defaultvalue = default, CultureInfo? culture = null)
        {
            return ObjectMapper.TryConvert(obj, defaultvalue, culture);
        }

        /// <summary>
        /// [扩展方法] 将对象序列化为JSON字符串，处理空值和字符串源对象场景
        /// </summary>
        /// <param name="self">待序列化的对象</param>
        /// <param name="settings">JSON序列化设置，可选，默认使用默认配置</param>
        /// <returns>
        /// 1. 源对象为null → 空字符串
        /// 2. 源对象为字符串类型 → 直接返回原字符串（不额外序列化）
        /// 3. 其他类型 → 调用Newtonsoft.Json序列化JSON字符串
        /// </returns>
        /// <remarks>序列化使用Newtonsoft.Json默认配置，如需自定义序列化规则需单独处理</remarks>
        [return: NotNull]
        public static string ToJson([AllowNull] this object self, JsonSerializerSettings? settings = null)
        {
            return self switch
            {
                null => string.Empty,
                string str => str,
                _ => JsonConvert.SerializeObject(self, settings)
            };
        }

        /// <summary>
        /// [扩展方法] 将JSON字符串反序列化为对象，处理空值和字符串源对象场景    
        /// </summary>
        /// <param name="self">待反序列化的JSON字符串</param>
        /// <returns>
        /// 1. 源对象为null → 空字符串
        /// 2. 源对象为字符串类型 → 直接返回原字符串（不额外序列化）
        /// </returns>
        /// <remarks>序列化使用Newtonsoft.Json默认配置，如需自定义序列化规则需单独处理</remarks>
        [return: MaybeNull]
        public static T DeserializeJson<T>([AllowNull] this string self, T? defalutValue = default, JsonSerializerSettings? settings = null)
        {
            if (self.IsNullOrEmpty())
            {
                return defalutValue;
            }

            return JsonConvert.DeserializeObject<T>(self, settings);
        }

        /// <summary>
        /// [扩展方法] 将泛型列表转换为DataTable
        /// </summary>
        /// <typeparam name="T">列表元素类型</typeparam>
        /// <param name="list">待转换的泛型列表</param>
        /// <returns>
        /// 1. 列表为null/空 → null
        /// 2. 列表有数据 → 转换后的DataTable（列名对应T的属性名）
        /// </returns>
        /// <remarks>底层调用DataTableMagic.ToDataTable实现，仅支持普通实体类属性映射</remarks>
        [return: MaybeNull]
        public static DataTable ToDataTable<T>([AllowNull] this IList<T> list)
        {
            return DataTableMagic.ToDataTable(list);
        }

        /// <summary>
        /// [扩展方法] 将DataTable转换为泛型列表
        /// </summary>
        /// <typeparam name="T">目标列表元素类型，需具备无参构造函数</typeparam>
        /// <param name="dt">待转换的DataTable</param>
        /// <returns>
        /// 1. DataTable为null/空 → null
        /// 2. DataTable有数据 → 转换后的泛型列表（属性名对应列名）
        /// </returns>
        /// <remarks>
        /// 1. 底层调用DataTableMagic.ToList<T>实现
        /// 2. T的属性名需与DataTable列名一致（大小写敏感）
        /// 3. T必须包含无参构造函数，否则转换失败
        /// </remarks>
        [return: MaybeNull]
        public static List<T> ToList<T>([AllowNull] this DataTable dt) where T : new()
        {
            return DataTableMagic.ToList<T>(dt);
        }
    }
}