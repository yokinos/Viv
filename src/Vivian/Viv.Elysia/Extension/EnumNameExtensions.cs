using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Viv.Elysia.Attributes;

namespace Viv.Elysia.Extension
{
    public static class EnumNameExtension
    {
        public static string GetEnumName(this Enum enumValue, string defaultName = "")
        {
            if (!Enum.IsDefined(enumValue.GetType(), enumValue))
            {
                return defaultName;
            }

            var field = enumValue.GetType().GetField(enumValue.ToString());
            if (field == null)
            {
                return defaultName;
            }

            var attribute = field.GetCustomAttribute<EnumNameAttribute>();
            return attribute?.Name ?? defaultName;
        }
    }
}
