using Newtonsoft.Json;
using System;

namespace Viv.Engine.Conveter
{
    public class JsonConverterLong : JsonConverter
    {
        /// <summary>
        /// 只处理 long / long? 类型
        /// </summary>
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(long) || objectType == typeof(long?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var value = reader.Value;

            // 空值处理
            if (value == null || value is DBNull)
                return objectType == typeof(long?) ? null : (long)0;

            // 空字符串处理
            if (value is string str && string.IsNullOrWhiteSpace(str))
                return objectType == typeof(long?) ? null : (long)0;

            // 安全转换
            if (long.TryParse(Convert.ToString(value), out long result))
                return result;

            // 转换失败返回默认值
            return objectType == typeof(long?) ? null : (long)0;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // long → 转字符串，防止前端精度丢失
            writer.WriteValue(value?.ToString() ?? string.Empty);
        }
    }
}