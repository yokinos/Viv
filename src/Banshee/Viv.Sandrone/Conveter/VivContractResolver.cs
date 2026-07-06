using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Viv.Sandrone.Conveter
{
    public class VivContractResolver : DefaultContractResolver
    {
        /// <summary>
        /// 长整型 JSON 转换器（单例，避免重复创建）
        /// </summary>
        private static readonly JsonConverterLong _longConverter = new JsonConverterLong();

        protected override JsonConverter ResolveContractConverter(Type objectType)
        {
            // 统一处理 long 和 long?
            if (objectType == typeof(long) || objectType == typeof(long?))
                return _longConverter;

            return base.ResolveContractConverter(objectType);
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            // 基础属性创建
            IList<JsonProperty> properties = base.CreateProperties(type, memberSerialization);

            // 仅对字符串属性应用 ValueProvider，避免不必要的开销
            foreach (var prop in properties.Where(p => p.PropertyType == typeof(string)))
            {
                var propertyInfo = type.GetProperty(prop.UnderlyingName!);
                if (propertyInfo != null)
                {
                    prop.ValueProvider = new NullToEmptyStringValueProvider(propertyInfo);
                }
            }

            return properties;
        }
    }
}