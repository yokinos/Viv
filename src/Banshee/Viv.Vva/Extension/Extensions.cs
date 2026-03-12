using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Text;
using Viv.Vva.Enums;
using Viv.Vva.Magic;

namespace Viv.Vva.Extension
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
                DateFormat.DateOnly => $"yyyy{symbol}MM{symbol}dd",
                DateFormat.LongDate => $"yyyy{symbol}MM{symbol}dd HH:mm:ss",
                DateFormat.CompactLongDate => "yyyyMMddHHmmss",
                DateFormat.TimeOnly => "HHmmss",
                DateFormat.StandardTime => "HH:mm:ss",
                _ => string.Empty
            };
        }

        /// <summary>
        /// [扩展方法] 深度拷贝对象（基于 JSON 序列化/反序列化实现）
        /// </summary>
        /// <typeparam name="T">目标类型，需具备无参构造函数</typeparam>
        /// <param name="t">待拷贝的对象</param>
        /// <returns>拷贝后的新对象；若原对象为 null 则返回 null</returns>
        /// <remarks>依赖 Newtonsoft.Json 实现深拷贝，仅适用于可序列化的类型</remarks>
        [return: MaybeNull]
        public static T DeepCopy<T>([AllowNull] this T t) where T : new()
        {
            return t.As<T>();
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

        /// <summary>
        /// [扩展方法] 空值替换（类似数据库 NVL 函数）
        /// </summary>
        /// <typeparam name="T">任意类型</typeparam>
        /// <param name="self">待检查的对象</param>
        /// <param name="otherValue">空值时的替换值</param>
        /// <returns>
        /// 1. 若 self 为 null/DBNull → 返回 otherValue
        /// 2. 否则 → 返回 self 本身
        /// </returns>
        public static T Nvl<T>([AllowNull] this T self, T otherValue)
        {
            return self is null or DBNull ? otherValue : self;
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
    }
}