using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Vva.Enum;

namespace Viv.Vva.Extension
{
    public static partial class Extensions
    {
        public static bool IsNullOrEmpty(this string self)
        {
            return string.IsNullOrWhiteSpace(self);
        }

        [return: NotNull]
        public static string ExtToString([AllowNull] this object self, Encoding? encoding = default)
        {
            return self switch
            {
                null => string.Empty,
                byte[] byteArray => (encoding ?? Encoding.UTF8).GetString(byteArray),
                _ => self.ToString()?.Trim() ?? string.Empty
            };
        }

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

        [return: MaybeNull]
        public static T DeepCopy<T>([AllowNull] this T t) where T : new()
        {
            return t.As<T>();
        }

        public static bool Between<T>(this T t, T min, T max) where T : IComparable<T>
        {
            return t.CompareTo(min) >= 0 && t.CompareTo(max) <= 0;
        }

        public 
    }
}
