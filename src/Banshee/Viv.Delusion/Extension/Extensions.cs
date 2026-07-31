using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Viv.Delusion;
using Viv.Delusion.Magic;

namespace Viv.Delusion.Extension
{
    /// <summary>
    /// 通用扩展方法类
    /// 包含字符串、对象、日期、枚举、数组等基础类型的便捷操作扩展
    /// </summary>
    public static partial class Extensions
    {
        /// <summary>
        /// [扩展方法] 判断字符串是否为 null、空字符串或仅包含空白字符
        /// </summary>
        /// <param name="self">待判断的字符串</param>
        /// <returns>true=字符串为空/空白；false=字符串非空且包含有效字符</returns>
        /// <remarks>
        /// 区别于原生 string.IsNullOrEmpty：本方法会同时判断空白字符（如空格、制表符）
        /// 等价于 string.IsNullOrWhiteSpace(self)
        /// </remarks>
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty([NotNullWhen(false)][AllowNull] this string self)
        {
            return string.IsNullOrWhiteSpace(self);
        }

        /// <summary>
        /// [扩展方法] 将任意对象转换为字符串，处理各类空值/特殊类型场景
        /// </summary>
        /// <param name="self">待转换的对象</param>
        /// <returns>
        /// 转换后的字符串：
        /// 1. null/DBNull → 空字符串
        /// 2. byte[] 数组 → UTF8 编码的字符串
        /// 3. 其他类型 → 调用 ToString() 并去除首尾空白，若结果为 null 则返回空字符串
        /// </returns>
        [return: NotNull]
        public static string ExtToString([AllowNull] this object self)
        {
            return self switch
            {
                null or DBNull => string.Empty,
                byte[] byteArray => Encoding.UTF8.GetString(byteArray),
                _ => self.ToString()?.Trim() ?? string.Empty
            };
        }

        /// <summary>
        /// [扩展方法] 将 DateTime 转换为指定格式的字符串，处理极值日期场景
        /// </summary>
        /// <param name="self">待转换的 DateTime 对象</param>
        /// <param name="formt">日期格式枚举（默认：带时分秒的完整日期）</param>
        /// <param name="symbol">日期分隔符（默认：-）</param>
        /// <returns>
        /// 格式化后的字符串：
        /// 1. 若为 DateTime.MinValue/MaxValue → 空字符串
        /// 2. 其他情况 → 按指定格式和分隔符生成字符串
        /// </returns>
        /// <example>
        /// 示例1：new DateTime(2026,2,11).ExtToString() → "2026-02-11 00:00:00"
        /// 示例2：new DateTime(2026,2,11).ExtToString(DateFormat.Date, "/") → "2026/02/11"
        /// </example>
        [return: NotNull]
        public static string ExtToString(this DateTime self, DateFormat formt = DateFormat.LongDate, string symbol = "-")
        {
            if (self == DateTime.MinValue || self == DateTime.MaxValue)
                return string.Empty;

            return formt switch
            {
                DateFormat.ShortDate => "yyyyMMdd",
                DateFormat.Date => $"yyyy{symbol}MM{symbol}dd",
                DateFormat.LongDate => $"yyyy{symbol}MM{symbol}dd HH:mm:ss",
                DateFormat.CompactLongDate => "yyyyMMddHHmmss",
                DateFormat.Time => "HHmmss",
                DateFormat.StandardTime => "HH:mm:ss",
                _ => string.Empty
            };
        }

        /// <summary>
        /// [扩展方法] 深度拷贝对象
        /// </summary>
        /// <typeparam name="T">目标类型，需具备无参构造函数</typeparam>
        /// <param name="source">待拷贝的对象</param>
        /// <returns></returns>
        [return: MaybeNull]
        public static T? DeepCopy<T>(this T source)
        {
            if (source == null) return default;

            var settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto
            };

            var json = JsonConvert.SerializeObject(source, settings);
            return JsonConvert.DeserializeObject<T>(json, settings);

            #region  示例

            var type = source.GetType();

            // 值类型/string 直接返回
            if (type.IsValueType || type == typeof(string))
                return source;

            // 数组
            if (type.IsArray)
            {
                var array = source as Array;
                var elementType = type.GetElementType()!;
                var copy = Array.CreateInstance(elementType, array.Length);
                for (int i = 0; i < array.Length; i++)
                    copy.SetValue(DeepCopy(array.GetValue(i)), i);
                return (T)(object)copy;
            }

            if (source is IDictionary dict)
            {
                // 尝试获取字典的泛型参数类型，用于创建同类型实例
                Type dictType;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    var args = type.GetGenericArguments();
                    var keyType = args[0];
                    var valueType = args[1];
                    dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                }
                else
                {
                    // 非泛型字典，默认使用 Dictionary<object, object>
                    dictType = typeof(Dictionary<object, object>);
                }
                var copy = (IDictionary)Activator.CreateInstance(dictType)!;
                foreach (DictionaryEntry entry in dict)
                {
                    var keyCopy = DeepCopy(entry.Key);
                    var valueCopy = DeepCopy(entry.Value);
                    copy[keyCopy] = valueCopy;
                }
                return (T)copy;
            }

            // 列表/集合
            if (typeof(IEnumerable<object>).IsAssignableFrom(type))
            {
                var listType = typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]);
                var list = Activator.CreateInstance(listType) as dynamic;
                if (list == null) return default;
                foreach (var item in source as dynamic)
                    list.Add(DeepCopy(item));
                return (T)list;
            }

            // 对象拷贝
            var obj = Activator.CreateInstance(type)!;
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                var value = prop.GetValue(source);
                prop.SetValue(obj, DeepCopy(value));
            }
            return (T)obj;

            #endregion
        }

        /// <summary>
        /// [扩展方法] 判断对象是否在指定的数值范围内（包含边界值）
        /// </summary>
        /// <typeparam name="T">可比较的类型（实现 IComparable<T>）</typeparam>
        /// <param name="self">待判断的对象</param>
        /// <param name="min">范围最小值</param>
        /// <param name="max">范围最大值</param>
        /// <returns>true=对象大于等于min且小于等于max；false=超出范围</returns>
        /// <example>
        /// 示例：5.Between(1, 10) → true；11.Between(1, 10) → false
        /// </example>
        public static bool Between<T>(this T self, T min, T max) where T : IComparable<T>
        {
            return self.CompareTo(min) >= 0 && self.CompareTo(max) <= 0;
        }

        public static bool Between(this IComparable self, object min, object max)
        {
            return self.CompareTo(min) >= 0 && self.CompareTo(max) <= 0;
        }

        /// <summary>
        /// [扩展方法] 空值替换（类似数据库 NVL 函数）
        /// </summary>
        /// <typeparam name="T">任意类型</typeparam>
        /// <param name="self">待检查的对象</param>
        /// <param name="defaultValue">为空/空字符串时的默认值</param>
        /// <returns>
        /// 1. 若 self 为 null/DBNull/空字符串 → 返回 defaultValue
        /// 2. 否则 → 返回 self 本身
        /// </returns>
        [return: NotNull]
        public static T Nvl<T>([AllowNull] this T self, [NotNull] T defaultValue)
        {
            ArgumentNullException.ThrowIfNull(defaultValue);
            if (self is null || self is DBNull || (self is string txt && txt.IsNullOrEmpty()))
            {
                return defaultValue;
            }
            else
            {
                return self;
            }
        }

        /// <summary>
        /// [扩展方法] 获取枚举值的描述信息（基于 EnumMagic 实现）
        /// </summary>
        /// <param name="self">待获取描述的枚举值</param>
        /// <returns>枚举值对应的描述字符串；若无描述则返回枚举名称</returns>
        public static string GetDescription(this Enum self)
        {
            return EnumMagic.GetDescription(self);
        }

        /// <summary>
        /// [扩展方法] 将对象转换为 UTF8 编码的字节数组（基于 JSON 序列化）
        /// </summary>
        /// <param name="self">待转换的对象</param>
        /// <param name="defaultvalue">对象为 null 时的默认返回值（默认：null）</param>
        /// <returns>
        /// 1. self 为 null → 返回 defaultvalue
        /// 2. 其他情况 → 先序列化为 JSON 字符串，再转换为 UTF8 字节数组
        /// </returns>
        [return: MaybeNull]
        public static byte[] ToBytes(this object self, byte[]? defaultvalue = default)
        {
            if (self is null) return defaultvalue;
            return Encoding.UTF8.GetBytes(self.ToJson());
        }

        /// <summary>
        /// [扩展方法] 获取指定类型的自定义 Attribute，未找到时返回 null
        /// </summary>
        /// <typeparam name="T">目标 Attribute 类型</typeparam>
        /// <param name="self">Type / MethodInfo / PropertyInfo / Assembly / ParameterInfo 等</param>
        /// <returns>
        /// 找到的 Attribute 实例：
        /// 1. 存在 → 返回第一个匹配的 T 类型实例（含继承链）
        /// 2. 不存在 → 返回 null
        /// </returns>
        /// <example>
        /// 示例1：type.GetAttribute&lt;VivCommandAttribute&gt;() → VivCommandAttribute 实例 或 null
        /// 示例2：method.GetAttribute&lt;HttpPostAttribute&gt;() → HttpPostAttribute 实例 或 null
        /// </example>
        [return: MaybeNull]
        public static T GetAttribute<T>(this ICustomAttributeProvider self) where T : Attribute
        {
            ArgumentNullException.ThrowIfNull(self);
            var attributes = self.GetCustomAttributes(typeof(T), true);
            return attributes.Length > 0 ? (T)attributes[0] : default;
        }
    }
}