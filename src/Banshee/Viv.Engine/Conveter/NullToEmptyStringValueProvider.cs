using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace Viv.Engine.Conveter
{
    /// <summary>
    /// 字符串属性值提供器：序列化时将 null 转为空字符串，反序列化时 Trim 字符串。
    /// </summary>
    public class NullToEmptyStringValueProvider : IValueProvider
    {
        private readonly PropertyInfo _propertyInfo;

        public NullToEmptyStringValueProvider(PropertyInfo propertyInfo)
        {
            _propertyInfo = propertyInfo;
        }

        public object? GetValue(object target)
        {
            object? value = _propertyInfo.GetValue(target);
            // 仅对字符串类型 null 值处理为空字符串
            if (_propertyInfo.PropertyType == typeof(string) && value == null)
            {
                return string.Empty;
            }
            return value;
        }

        public void SetValue(object target, object? value)
        {
            // 如果值是字符串且不为 null，执行 Trim
            if (value is string str)
            {
                value = str.Trim();
            }
            _propertyInfo.SetValue(target, value);
        }
    }
}