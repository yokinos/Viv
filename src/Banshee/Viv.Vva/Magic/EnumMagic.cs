using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Viv.Vva.Extension;
using Viv.Vva.Generic;

namespace Viv.Vva.Magic
{
    public static class EnumMagic
    {
        /// <summary>
        /// 将指定枚举类型转换成 List
        /// </summary>
        [return: MaybeNull]
        public static List<KeyValueItem<string, int>> EnumToList(Type type)
        {
            if (type == null || !type.IsEnum) return default;
            var list = new List<KeyValueItem<string, int>>();
            foreach (var val in Enum.GetValues(type))
            {
                var name = Enum.GetName(type, val) ?? val.ToString();
                if (name.IsNullOrEmpty()) continue;
                var fi = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                var key = TryGetDescription(fi);
                int intVal;
                try
                {
                    intVal = Convert.ToInt32(val);
                }
                catch
                {
                    intVal = unchecked((int)Convert.ToInt64(val));
                }

                list.Add(new KeyValueItem<string, int>(key, intVal));
            }

            return list;
        }

        /// <summary>
        /// 获取枚举项的 DescriptionAttribute 或名称
        /// </summary>
        public static string GetDescription(Enum enumValue)
        {
            if (enumValue == null) return string.Empty;
            var type = enumValue.GetType();
            var fi = type.GetField(enumValue.ToString(), BindingFlags.Public | BindingFlags.Static);
            return TryGetDescription(fi);
        }

        private static string TryGetDescription(FieldInfo? field)
        {
            if (field == null) return string.Empty;
            var attr = field.GetCustomAttribute<DescriptionAttribute>(false);
            return attr?.Description ?? field.Name;
        }

        [return: MaybeNull]
        public static T Parse<T>(object member) where T : struct
        {
            if (member == null) return default;
            if (member is T tVal) return tVal;

            var targetType = typeof(T);
            if (member is string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return default;
                if (Enum.TryParse<T>(s, true, out var parsedByName)) return parsedByName;
                if (long.TryParse(s, out var longVal))
                {
                    var enumObj = Enum.ToObject(targetType, longVal);
                    return (T)enumObj;
                }
                return default;
            }

            if (member is IConvertible)
            {
                var underlying = Enum.GetUnderlyingType(targetType);
                var converted = Convert.ChangeType(member, underlying);
                var enumObj = Enum.ToObject(targetType, converted!);
                return (T)enumObj;
            }

            var ms = member.ToString();
            if (!string.IsNullOrEmpty(ms) && Enum.TryParse<T>(ms, true, out var parsed))
            {
                return parsed;
            }

            return default;
        }
    }
}
