using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Elysia.Interface;

namespace Viv.Elysia.Request
{
    /// <summary>
    /// 实体模型校验工具
    /// </summary>
    public static class RequestParameterValidator
    {
        public static string Validate(object obj, HashSet<object> validatingObjects)
        {
            if (obj == null)
            {
                return "校验对象不能为 null";
            }

            var objectType = obj.GetType();

            if (IsSimpleType(objectType))
            {
                return string.Empty;
            }

            // 防止对象之间相互引用导致无限递归
            if (!objectType.IsValueType)
            {
                if (validatingObjects.Contains(obj))
                {
                    return string.Empty;
                }

                validatingObjects.Add(obj);
            }

            try
            {
                // 先执行类型级别校验
                var typeValidationError = ValidateTypeAttributes(obj);
                if (!string.IsNullOrEmpty(typeValidationError))
                {
                    return typeValidationError;
                }

                var properties = VivTypeReflectionCache.GetPropertieList(objectType);
                if (properties.IsNullOrEmpty())
                {
                    return ValidateObjectSelf(obj);
                }

                foreach (var property in properties)
                {
                    if (property == null || property.GetIndexParameters().Length > 0 || property.GetMethod == null)
                    {
                        continue;
                    }

                    object? value;

                    try
                    {
                        value = property.GetValue(obj);
                    }
                    catch (Exception ex)
                    {
                        return $"{GetDisplayName(property)} 读取失败：{ex.Message}";
                    }

                    var displayName = GetDisplayName(property);

                    // 先递归校验复杂对象和集合对象
                    if (value != null)
                    {
                        var nestedError = ValidateNestedValue(value, validatingObjects);
                        if (!string.IsNullOrEmpty(nestedError))
                        {
                            return nestedError;
                        }
                    }

                    // 再执行当前属性上的 DataAnnotations 校验
                    var propertyError = ValidateProperty(obj, property, value, displayName);

                    if (!string.IsNullOrEmpty(propertyError))
                    {
                        return propertyError;
                    }
                }

                // 最后执行 IValidatableObject 校验
                return ValidateObjectSelf(obj);
            }
            finally
            {
                if (!objectType.IsValueType)
                {
                    validatingObjects.Remove(obj);
                }
            }
        }

        /// <summary>
        /// 校验嵌套对象、IApiRequest 或集合元素。
        /// </summary>
        private static string ValidateNestedValue(object value, HashSet<object> validatingObjects)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is IApiRequest request)
            {
                return request.Validate(true) ?? string.Empty;
            }

            if (value is IEnumerable enumerable &&
                value is not string)
            {
                foreach (var item in enumerable)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var itemError = ValidateNestedValue(item, validatingObjects);
                    if (!string.IsNullOrEmpty(itemError))
                    {
                        return itemError;
                    }
                }

                return string.Empty;
            }

            if (!IsCustomModelType(value.GetType()))
            {
                return string.Empty;
            }

            return Validate(value, validatingObjects);
        }

        /// <summary>
        /// 校验属性上的所有 ValidationAttribute。
        /// </summary>
        private static string ValidateProperty(object obj, PropertyInfo property, object value, string displayName)
        {
            var attributes = property.GetCustomAttributes(typeof(ValidationAttribute), true).OfType<ValidationAttribute>().ToArray();
            if (attributes.Length == 0)
            {
                return string.Empty;
            }

            var context = new ValidationContext(obj)
            {
                MemberName = property.Name,
                DisplayName = displayName
            };

            foreach (var attribute in attributes)
            {
                ValidationResult? result;

                try
                {
                    result = attribute.GetValidationResult(value, context);
                }
                catch (Exception ex)
                {
                    return $"{displayName} 校验失败：{ex.Message}";
                }

                if (result != ValidationResult.Success)
                {
                    return GetValidationErrorMessage(result, attribute, displayName);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 校验类型上的 ValidationAttribute。
        /// </summary>
        private static string ValidateTypeAttributes(object obj)
        {
            var objectType = obj.GetType();
            var attributes = objectType.GetCustomAttributes(typeof(ValidationAttribute), true).OfType<ValidationAttribute>().ToArray();

            if (attributes.Length == 0)
            {
                return string.Empty;
            }

            var context = new ValidationContext(obj)
            {
                DisplayName = objectType.Name
            };

            foreach (var attribute in attributes)
            {
                var result = attribute.GetValidationResult(obj, context);

                if (result != ValidationResult.Success)
                {
                    return GetValidationErrorMessage(result, attribute, objectType.Name);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 校验 IValidatableObject。
        /// </summary>
        private static string ValidateObjectSelf(object obj)
        {
            if (obj is not IValidatableObject validatableObject)
            {
                return string.Empty;
            }

            var context = new ValidationContext(obj);
            var results = validatableObject.Validate(context)?.ToList();

            if (results == null || results.Count == 0)
            {
                return string.Empty;
            }

            var firstResult = results.FirstOrDefault(x => x != ValidationResult.Success);
            return firstResult?.ErrorMessage ?? $"{obj.GetType().Name} 校验失败";
        }

        /// <summary>
        /// 获取最终错误信息。
        /// </summary>
        private static string GetValidationErrorMessage(ValidationResult result, ValidationAttribute attribute, string displayName)
        {
            if (!string.IsNullOrWhiteSpace(result?.ErrorMessage))
            {
                return result.ErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(attribute.ErrorMessage))
            {
                return attribute.ErrorMessage;
            }

            return $"{displayName} 校验失败";
        }

        /// <summary>
        /// 获取属性友好名称。
        ///
        /// 优先级：
        /// 1. DisplayNameAttribute；
        /// 2. DisplayAttribute；
        /// 3. 属性名。
        /// </summary>
        private static string GetDisplayName(PropertyInfo property)
        {
            var displayNameAttribute = property.GetCustomAttribute<DisplayNameAttribute>();
            if (!string.IsNullOrWhiteSpace(displayNameAttribute?.DisplayName))
            {
                return displayNameAttribute.DisplayName;
            }

            var displayAttribute = property.GetCustomAttribute<DisplayAttribute>();
            var displayName = displayAttribute?.GetName();

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            return property.Name;
        }

        /// <summary>
        /// 判断是否为简单类型。
        /// 简单类型不进行递归校验。
        /// </summary>
        private static bool IsSimpleType(Type type)
        {
            if (type == null)
            {
                return true;
            }

            var nullableType = Nullable.GetUnderlyingType(type);

            if (nullableType != null)
            {
                type = nullableType;
            }

            return type.IsPrimitive
                || type.IsEnum
                || type.IsValueType
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(Guid)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(byte[]);
        }

        /// <summary>
        /// 判断是否为自定义模型类型。
        /// </summary>
        private static bool IsCustomModelType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (IsSimpleType(type))
            {
                return false;
            }

            if (type.IsArray)
            {
                return false;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 使用对象引用判断相等，避免对象重写 Equals 后影响循环引用判断。
        /// </summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            private ReferenceEqualityComparer()
            {

            }

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}