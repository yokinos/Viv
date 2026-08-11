using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Viv.Forge
{
    /// <summary>
    /// 源生成器静态工具 —— 特性参数读取、类型全名/可空性、标识符清理与字符串字面量转义。
    /// </summary>
    public static class SourceGenHelpers
    {
        /// <summary>取特性构造参数（按位置）；越界或值类型不同返回 null。</summary>
        public static TypedConstant? GetAttributeArg(AttributeData attr, int index)
        {
            var args = attr.ConstructorArguments;
            if (index < 0 || index >= args.Length) return null;
            return args[index];
        }

        /// <summary>取特性命名参数；不存在返回 null。</summary>
        public static TypedConstant? GetNamedArg(AttributeData attr, string name)
        {
            foreach (var pair in attr.NamedArguments)
            {
                if (pair.Key == name) return pair.Value;
            }
            return null;
        }

        /// <summary>构造参数转字符串（含 C# 转义）；不是字符串类型返回 null。</summary>
        public static string? GetAttributeArgString(AttributeData attr, int index)
        {
            var arg = GetAttributeArg(attr, index);
            return arg?.Value is string s ? s : null;
        }

        /// <summary>命名参数转字符串（含 C# 转义）；不是字符串类型返回 null。</summary>
        public static string? GetNamedArgString(AttributeData attr, string name)
        {
            var arg = GetNamedArg(attr, name);
            return arg?.Value is string s ? s : null;
        }

        /// <summary>构造参数转 int；解析失败或类型不符返回 null。</summary>
        public static int? GetAttributeArgInt(AttributeData attr, int index)
        {
            var arg = GetAttributeArg(attr, index);
            return arg?.Value is int i ? i : (int?)null;
        }

        /// <summary>类型全限定名（global:: 前缀），用于生成的代码里无歧义引用。</summary>
        public static string FullyQualifiedName(ITypeSymbol type)
            => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        /// <summary>类型符号转带 nullable 的全限定名（对 T? 引用类型返回 "global::T?"）。</summary>
        public static string FullyQualifiedNameWithNullable(ITypeSymbol type)
        {
            var name = FullyQualifiedName(type);
            return type.NullableAnnotation == NullableAnnotation.Annotated ? name + "?" : name;
        }

        /// <summary>把任意字符串转成合法 C# 标识符（字母数字下划线之外替换为 _，空串给 fallback）。</summary>
        public static string SanitizeIdentifier(string name, string fallback = "Generated")
        {
            if (string.IsNullOrEmpty(name)) return fallback;

            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            var result = sb.ToString();
            // 数字开头的非法标识符补前缀
            if (result.Length > 0 && char.IsDigit(result[0]))
            {
                result = "_" + result;
            }
            return result.Length > 0 ? result : fallback;
        }

        /// <summary>字符串字面量转义（\\ \" \n \r \t），输出含外层双引号。</summary>
        public static string EscapeString(string value)
        {
            if (value == null) return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>符号上的可写属性（有 setter）列表，供生成器反射生成赋值代码用。</summary>
        public static ImmutableArray<IPropertySymbol> GetWritableProperties(ITypeSymbol type)
        {
            var builder = ImmutableArray.CreateBuilder<IPropertySymbol>();
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol { SetMethod: not null } prop)
                {
                    builder.Add(prop);
                }
            }
            return builder.ToImmutable();
        }
    }
}
