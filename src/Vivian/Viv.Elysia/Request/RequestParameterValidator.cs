using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Viv.Delusion;
using Viv.Elysia.Interface;
using Viv.Delusion.Extension;

namespace Viv.Elysia.Request
{
    /// <summary>
    /// 实体模型校验工具（遇到第一个错误立即返回）
    /// 自动从 [Display(Name="")] 读取友好字段名
    /// </summary>
    public static class RequestParameterValidator
    {
        /// <summary>
        /// 校验对象，返回第一条错误信息
        /// </summary>
        public static string Validate(object obj)
        {
            if (obj == null)
                return "校验对象不能为 null";

            var properties = VivTypeReflectionCache.GetPropertieList(obj.GetType());
            if (properties.IsNullOrEmpty())
                return string.Empty;

            foreach (var property in properties)
            {
                var attributes = property.GetCustomAttributes(true);
                if (attributes.IsNullOrEmpty())
                    continue;

                object? value = property.GetValue(obj);
                if (value != null && value is IApiRequest request)
                {
                    var msg = request.Validate(true);
                    if (!string.IsNullOrEmpty(msg))
                    {
                        return msg;
                    }
                }
                else
                {
                    if (value != null && IsCustomModelType(property.PropertyType))
                    {
                        string nestedError = Validate(value);
                        if (!string.IsNullOrEmpty(nestedError))
                            return nestedError;
                    }
                }

                // 读取友好名称：优先 [Display(Name)]，没有则用属性名
                var displayName = GetDisplayName(property);
                foreach (var attr in attributes)
                {
                    // 必填
                    if (attr is RequiredAttribute required)
                    {
                        bool isEmpty = value is null || value == DBNull.Value || string.IsNullOrWhiteSpace(value.ToString());
                        if (isEmpty)
                        {
                            return required.ErrorMessage.Nvl($"{displayName} 不能为空");
                        }
                    }

                    // 字符串长度
                    if (attr is StringLengthAttribute strLen)
                    {
                        if (value is string strValue)
                        {
                            bool invalid = strValue.Length < strLen.MinimumLength || strValue.Length > strLen.MaximumLength;
                            if (invalid)
                            {
                                return strLen.ErrorMessage.Nvl($"{displayName} 长度必须在 {strLen.MinimumLength} ~ {strLen.MaximumLength} 之间");
                            }
                        }
                    }

                    // 范围
                    if (attr is RangeAttribute range && value is IComparable comparable)
                    {
                        if (!range.IsValid(comparable))
                        {
                            return range.ErrorMessage.Nvl($"{displayName} 必须在 {range.Minimum} ~ {range.Maximum} 之间");
                        }
                    }

                    // 最小长度
                    if (attr is MinLengthAttribute minLen)
                    {
                        if (value is string strVal && strVal.Length < minLen.Length)
                        {
                            return minLen.ErrorMessage.Nvl($"{displayName} 长度不能小于 {minLen.Length} 位");
                        }
                    }

                    // 最大长度
                    if (attr is MaxLengthAttribute maxLen)
                    {
                        if (value is string strVal && strVal.Length > maxLen.Length)
                        {
                            return maxLen.ErrorMessage.Nvl($"{displayName} 长度不能超过 {maxLen.Length} 位");
                        }
                    }

                    // 正则
                    if (attr is RegularExpressionAttribute regex)
                    {
                        if (value is string strVal && !string.IsNullOrEmpty(strVal))
                        {
                            if (!System.Text.RegularExpressions.Regex.IsMatch(strVal, regex.Pattern))
                            {
                                return regex.ErrorMessage.Nvl($"{displayName} 格式不正确");
                            }
                        }
                    }

                    // 邮箱
                    if (attr is EmailAddressAttribute email)
                    {
                        if (value is string strVal && !string.IsNullOrEmpty(strVal))
                        {
                            if (!email.IsValid(strVal))
                            {
                                return email.ErrorMessage.Nvl($"{displayName} 格式不正确");
                            }
                        }
                    }

                    // 手机
                    if (attr is PhoneAttribute phone)
                    {
                        if (value is string strVal && !string.IsNullOrEmpty(strVal))
                        {
                            if (!phone.IsValid(strVal))
                            {
                                return phone.ErrorMessage.Nvl($"{displayName} 格式不正确");
                            }
                        }
                    }

                    // URL
                    if (attr is UrlAttribute url)
                    {
                        if (value is string strVal && !string.IsNullOrEmpty(strVal))
                        {
                            if (!url.IsValid(strVal))
                            {
                                return url.ErrorMessage.Nvl($"{displayName} 格式不正确");
                            }
                        }
                    }

                    // 比较
                    if (attr is CompareAttribute compare)
                    {
                        var otherProp = obj.GetType().GetProperty(compare.OtherProperty);
                        if (otherProp != null)
                        {
                            string otherName = GetDisplayName(otherProp);
                            var otherValue = otherProp.GetValue(obj)?.ToString() ?? "";
                            var curValue = value?.ToString() ?? "";

                            if (curValue != otherValue)
                            {
                                return compare.ErrorMessage.Nvl($"{displayName} 与 {otherName} 不一致");
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取属性的友好显示名称（从 [Display(Name)] 读取）
        /// </summary>
        private static string GetDisplayName(PropertyInfo prop)
        {
            var display = prop.GetCustomAttribute<DisplayAttribute>();
            return string.IsNullOrEmpty(display?.Name) ? prop.Name : display.Name;
        }

        private static bool IsCustomModelType(Type type)
        {
            if (type == null) return false;
            if (type.IsPrimitive) return false;
            if (type == typeof(string)) return false;
            if (type.IsValueType) return false;
            if (type.IsArray) return false;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return false;
            return true;
        }
    }
}