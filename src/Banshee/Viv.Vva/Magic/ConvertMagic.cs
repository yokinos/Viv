using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Viv.Vva.Extension;

namespace Viv.Vva.Magic
{
    /// <summary>
    /// 通用类型转换工具类（核心能力：Unix时间戳转换、多类型安全转换）
    /// </summary>
    /// <remarks>
    /// 设计原则：
    /// 1. 无侵入：转换失败返回默认值，不抛业务异常；
    /// 2. 兼容：支持常见基础类型、枚举、DateTime/DateTimeOffset、JSON字符串转换；
    /// 3. 鲁棒：自动处理时区、Unix时间戳（秒/毫秒）、多语言文化格式；
    /// 4. 轻量：仅依赖Newtonsoft.Json，无其他第三方依赖。
    /// </remarks>
    public static class ConvertMagic
    {
        /// <summary>Unix时间纪元（1970-01-01 00:00:00 UTC）</summary>
        private static readonly DateTimeOffset _unixEpoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        /// <summary>Unix时间纪元（DateTime UTC版本）</summary>
        private static readonly DateTime _unixEpochDateTimeUtc = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        /// <summary>Unix时间戳秒/毫秒判断阈值（1万亿：超过则判定为毫秒级）</summary>
        private const long MillisecondsThreshold = 1_000_000_000_000L;
        /// <summary>布尔值识别关键字（忽略大小写）</summary>
        private static readonly HashSet<string> _trueKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "是", "对", "正确", "YES", "OK", "1", "成功", "Y"
        };
        /// <summary>字符串转基础类型的解析器映射表</summary>
        private static readonly Dictionary<Type, Func<string, CultureInfo, object?, object?>> stringParsers;

        static ConvertMagic()
        {
            stringParsers = new Dictionary<Type, Func<string, CultureInfo, object?, object?>>
            {
                [typeof(string)] = (s, c, d) => s,
                [typeof(char)] = (s, c, d) => char.TryParse(s, out var ch) ? ch : d,
                [typeof(sbyte)] = (s, c, d) => sbyte.TryParse(s, NumberStyles.Any, c, out var s8) ? s8 : d,
                [typeof(short)] = (s, c, d) => short.TryParse(s, NumberStyles.Any, c, out var i16) ? i16 : d,
                [typeof(int)] = (s, c, d) => int.TryParse(s, NumberStyles.Any, c, out var i32) ? i32 : d,
                [typeof(long)] = (s, c, d) => long.TryParse(s, NumberStyles.Any, c, out var i64) ? i64 : d,
                [typeof(byte)] = (s, c, d) => byte.TryParse(s, NumberStyles.Any, c, out var u8) ? u8 : d,
                [typeof(ushort)] = (s, c, d) => ushort.TryParse(s, NumberStyles.Any, c, out var u16) ? u16 : d,
                [typeof(uint)] = (s, c, d) => uint.TryParse(s, NumberStyles.Any, c, out var u32) ? u32 : d,
                [typeof(ulong)] = (s, c, d) => ulong.TryParse(s, NumberStyles.Any, c, out var u64) ? u64 : d,
                [typeof(float)] = (s, c, d) => float.TryParse(s, NumberStyles.Any, c, out var f) ? f : d,
                [typeof(double)] = (s, c, d) => double.TryParse(s, NumberStyles.Any, c, out var dd) ? dd : d,
                [typeof(decimal)] = (s, c, d) => decimal.TryParse(s, NumberStyles.Any, c, out var dec) ? dec : d,
                [typeof(bool)] = (s, c, d) =>
                {
                    return bool.TryParse(s, out var b) ? b : (_trueKeywords.Contains(s));
                },
                [typeof(DateTime)] = (s, c, d) => ParseDateTimeInternal(s, c) ?? d,
                [typeof(DateTimeOffset)] = (s, c, d) => ParseDateTimeOffsetInternal(s, c) ?? d,
                [typeof(Guid)] = (s, c, d) => Guid.TryParse(s, out var g) ? g : d,
                [typeof(byte[])] = (s, c, d) => Encoding.UTF8.GetBytes(s)
            };
        }

        /// <summary>
        /// 将DateTime转换为Unix时间戳（自动统一为UTC时区）
        /// </summary>
        /// <param name="dateTime">待转换时间（支持Utc/Local/Unspecified）</param>
        /// <param name="isMilliseconds">是否返回毫秒级时间戳（默认秒级）</param>
        /// <returns>Unix时间戳（1970年前返回负数，不做拦截）</returns>
        public static long ToUnixTime(DateTime dateTime, bool isMilliseconds = false)
        {
            var utcDateTime = dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
                _ => dateTime
            };

            var dateTimeOffset = new DateTimeOffset(utcDateTime);
            return ToUnixTime(dateTimeOffset, isMilliseconds);
        }

        /// <summary>
        /// 将DateTimeOffset转换为Unix时间戳
        /// </summary>
        /// <param name="date">带时区偏移的时间</param>
        /// <param name="isMilliseconds">是否返回毫秒级时间戳（默认秒级）</param>
        /// <returns>Unix时间戳（1970年前返回负数）</returns>
        public static long ToUnixTime(DateTimeOffset date, bool isMilliseconds = false)
        {
            return isMilliseconds ? date.ToUnixTimeMilliseconds() : date.ToUnixTimeSeconds();
        }

        /// <summary>
        /// 安全类型转换（失败返回默认值，无业务异常）
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="source">源对象（支持DBNull/Null、基础类型、JSON字符串等）</param>
        /// <param name="defaultValue">转换失败时返回的默认值</param>
        /// <param name="culture">文化格式（默认不变文化）</param>
        /// <returns>转换后的值，失败返回defaultValue</returns>
        [return: MaybeNull]
        public static T TryConvert<T>(object? source, T defaultValue, CultureInfo? culture = null)
        {
            if (source is null or DBNull) return defaultValue;
            if (source is T matched) return matched;

            try
            {
                var cultureInfo = culture ?? CultureInfo.InvariantCulture;
                var sourceType = source.GetType();
                var targetType = typeof(T);

                // 原始数值类型间直接转换
                if (sourceType.IsPrimitive && targetType.IsPrimitive)
                {
                    return (T)Convert.ChangeType(source, targetType, cultureInfo);
                }

                // 枚举由数值转换
                if (targetType.IsEnum && sourceType.IsPrimitive)
                {
                    return (T)Enum.ToObject(targetType, source);
                }

                var sourceText = ConvertObjectToString(source, sourceType);
                if (string.IsNullOrEmpty(sourceText)) return defaultValue;

                var converted = TryConvertStringToType(sourceText, targetType, cultureInfo, defaultValue);
                if (converted is not null)
                {
                    return (T)converted;
                }

                return JsonConvert.DeserializeObject<T>(sourceText);
            }
            catch (Exception ex)
            {
                LogConversionError(source, typeof(T), ex);
                return defaultValue;
            }
        }

        /// <summary>
        /// 尝试将字符串解析为指定类型
        /// </summary>
        /// <param name="sourceText">源字符串</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="culture">文化格式</param>
        /// <param name="defaultValue">解析失败默认值</param>
        /// <returns>解析结果，失败返回null</returns>
        private static object? TryConvertStringToType(string sourceText, Type targetType, CultureInfo culture, object? defaultValue)
        {
            if (targetType.IsEnum)
            {
                return Enum.TryParse(targetType, sourceText, true, out var enumResult) ? enumResult : defaultValue;
            }

            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying is not null)
            {
                var underlyingResult = TryConvertStringToType(sourceText, underlying, culture, defaultValue);
                if (underlyingResult is null) return defaultValue;
                return underlyingResult;
            }

            if (stringParsers.TryGetValue(targetType, out var parser))
            {
                return parser(sourceText, culture, defaultValue);
            }

            return null;
        }

        /// <summary>
        /// 将任意对象转换为字符串（适配不同类型的序列化规则）
        /// </summary>
        /// <param name="source">源对象</param>
        /// <param name="sourceType">源对象类型</param>
        /// <returns>序列化后的字符串</returns>
        private static string ConvertObjectToString(object source, Type sourceType)
        {
            return source switch
            {
                string str => str,
                Enum enumVal => enumVal.ToString(),
                byte[] byteArr => Encoding.UTF8.GetString(byteArr),
                Guid guid => guid.ToString(),
                _ when sourceType.IsPrimitive || sourceType == typeof(decimal) => Convert.ToString(source, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => JsonConvert.SerializeObject(source)
            };
        }

        /// <summary>
        /// 内部方法：解析字符串为DateTime（支持Unix时间戳/常规时间格式）
        /// </summary>
        /// <param name="sourceText">源字符串</param>
        /// <param name="culture">文化格式</param>
        /// <returns>解析后的DateTime，失败返回null</returns>
        private static DateTime? ParseDateTimeInternal(string sourceText, CultureInfo culture)
        {
            if (long.TryParse(sourceText, out var unixTime))
            {
                return unixTime > MillisecondsThreshold ? _unixEpochDateTimeUtc.AddMilliseconds(unixTime).ToLocalTime() : _unixEpochDateTimeUtc.AddSeconds(unixTime).ToLocalTime();
            }

            return DateTime.TryParse(sourceText, culture, DateTimeStyles.None, out var dt) ? dt : null;
        }

        /// <summary>
        /// 内部方法：解析字符串为DateTimeOffset（支持Unix时间戳/常规时间格式）
        /// </summary>
        /// <param name="sourceText">源字符串</param>
        /// <param name="culture">文化格式</param>
        /// <returns>解析后的DateTimeOffset，失败返回null</returns>
        private static DateTimeOffset? ParseDateTimeOffsetInternal(string sourceText, CultureInfo culture)
        {
            if (long.TryParse(sourceText, out var unixTime))
            {
                return unixTime > MillisecondsThreshold ? _unixEpoch.AddMilliseconds(unixTime).ToLocalTime() : _unixEpoch.AddSeconds(unixTime).ToLocalTime();
            }

            return DateTimeOffset.TryParse(sourceText, culture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto) ? dto : null;
        }

        private static void LogConversionError(object? source, Type targetType, Exception ex)
        {
            var sourceText = source.ToJson();
            var errorLog = $"【类型转换失败】源类型:{source?.GetType().FullName ?? "未知"} 源值:{sourceText} 目标类型:{targetType.FullName} 异常:{ex.Message}";
            Console.WriteLine(errorLog);
        }
    }
}