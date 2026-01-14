using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Viv.Vva.Extension;

namespace Viv.Vva.Magic
{
    public static class ConvertMagic
    {
        private static readonly DateTimeOffset _unixEpoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly DateTime _unixEpochDateTimeUtc = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private const long MillisecondsThreshold = 1_000_000_000_000L;
        private static readonly HashSet<string> _trueKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "是", "对", "正确", "YES", "OK", "1", "成功", "Y"
        };

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

        public static long ToUnixTime(DateTime dateTime, bool isMilliseconds = false)
        {
            var dto = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            var timeSpan = dto - _unixEpoch;
            return isMilliseconds ? (long)timeSpan.TotalMilliseconds : (long)timeSpan.TotalSeconds;
        }

        [return: MaybeNull]
        public static T TryConvert<T>([AllowNull] object? source, T? defaultValue = default, CultureInfo? culture = null)
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

        private static DateTime? ParseDateTimeInternal(string sourceText, CultureInfo culture)
        {
            if (long.TryParse(sourceText, out var unixTime))
            {
                return unixTime > MillisecondsThreshold
                    ? _unixEpochDateTimeUtc.AddMilliseconds(unixTime).ToLocalTime()
                    : _unixEpochDateTimeUtc.AddSeconds(unixTime).ToLocalTime();
            }

            return DateTime.TryParse(sourceText, culture, DateTimeStyles.None, out var dt) ? dt : null;
        }

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