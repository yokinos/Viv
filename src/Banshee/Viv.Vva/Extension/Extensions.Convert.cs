using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Viv.Vva.Magic;

namespace Viv.Vva.Extension
{
    public static partial class Extensions
    {
        public static long ToUnixTime(this DateTime self, bool isMs = false)
        {
            return ConvertMagic.ToUnixTime(self, isMs);
        }

        [return: MaybeNull]
        public static T As<T>([AllowNull] this object obj, T? defaultvalue = default, CultureInfo? culture = null)
        {
            return ConvertMagic.TryConvert(obj, defaultvalue, culture);
        }

        [return: NotNull]
        public static string ToJson([AllowNull] this object self)
        {
            return self switch
            {
                null => string.Empty,
                string str => str,
                _ => JsonConvert.SerializeObject(self)
            };
        }

        [return: MaybeNull]
        public static DataTable ToDataTable<T>([AllowNull]this IList<T> list)
        {
            return DataTableMagic.ToDataTable(list);
        }

        [return: MaybeNull]
        public static List<T> ToList<T>([AllowNull] this DataTable dt) where T : new()
        {
            return DataTableMagic.ToList<T>(dt);
        }
    }
}
