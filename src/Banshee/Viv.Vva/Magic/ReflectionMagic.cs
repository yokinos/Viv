using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Viv.Vva.Magic
{
    public class ReflectionMagic
    {
        [return: MaybeNull]
        public static T GetAttribute<T>(ICustomAttributeProvider provider) where T : Attribute
        {
            ArgumentNullException.ThrowIfNull(provider);
            var attributes = provider.GetCustomAttributes(typeof(T), true);
            return attributes.Length > 0 ? (T)attributes[0] : default;
        }
    }
}
