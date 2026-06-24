using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;

namespace Viv.Engine
{
    public sealed class VivOpenApiSchemaTransformer : IOpenApiSchemaTransformer
    {
        private static readonly ConcurrentDictionary<Assembly, XDocument?> XmlCache = new();

        public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
        {
            if (context?.JsonTypeInfo is null)
                return Task.CompletedTask;

            var type = context.JsonTypeInfo.Type;
            if (type is null)
                return Task.CompletedTask;

            var xml = LoadXml(type.Assembly);
            if (xml is null)
                return Task.CompletedTask;

            ApplyTypeDescription(schema, xml, type);
            ApplyPropertyDescriptions(schema, context.JsonTypeInfo, xml);

            return Task.CompletedTask;
        }

        private static void ApplyTypeDescription(OpenApiSchema schema, XDocument xml, Type type)
        {
            var member = FindMember(xml, $"T:{GetTypeDocName(type, includeGenericParameters: false)}");
            if (member is null)
                return;

            var description = CombineDescription(
                Normalize(member.Element("summary")?.Value),
                Normalize(member.Element("remarks")?.Value));

            if (!string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(schema.Description))
                schema.Description = description;
        }

        private static void ApplyPropertyDescriptions(OpenApiSchema schema, JsonTypeInfo typeInfo, XDocument xml)
        {
            if (schema.Properties is null || schema.Properties.Count == 0)
                return;

            foreach (var jsonProperty in typeInfo.Properties)
            {
                if (jsonProperty?.AttributeProvider is not PropertyInfo propertyInfo)
                    continue;

                var description = GetPropertyDescription(xml, propertyInfo);
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                foreach (var candidate in GetCandidateKeys(propertyInfo))
                {
                    if (schema.Properties.TryGetValue(candidate, out var propertySchema) &&
                        string.IsNullOrWhiteSpace(propertySchema.Description))
                    {
                        propertySchema.Description = description;
                        break;
                    }
                }
            }
        }

        private static IEnumerable<string> GetCandidateKeys(PropertyInfo prop)
        {
            yield return prop.Name;

            var camel = char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
            if (!string.Equals(camel, prop.Name, StringComparison.Ordinal))
                yield return camel;

            var jsonName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            if (!string.IsNullOrWhiteSpace(jsonName))
                yield return jsonName!;

            var newtonsoftName = prop.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>()?.PropertyName;
            if (!string.IsNullOrWhiteSpace(newtonsoftName))
                yield return newtonsoftName!;
        }

        private static string? GetPropertyDescription(XDocument xml, PropertyInfo propertyInfo)
        {
            var declaringType = propertyInfo.DeclaringType;
            if (declaringType is null)
                return null;

            var member = FindMember(xml, $"P:{GetTypeDocName(declaringType, includeGenericParameters: false)}.{propertyInfo.Name}");
            if (member is null)
                return null;

            return CombineDescription(
                Normalize(member.Element("summary")?.Value),
                Normalize(member.Element("remarks")?.Value));
        }

        private static string CombineDescription(string summary, string remarks)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return remarks;

            if (string.IsNullOrWhiteSpace(remarks))
                return summary;

            return $"{summary}\n\n{remarks}";
        }

        private static XDocument? LoadXml(Assembly assembly)
        {
            return XmlCache.GetOrAdd(assembly, asm =>
            {
                var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.xml");
                return File.Exists(xmlPath) ? XDocument.Load(xmlPath) : null;
            });
        }

        private static XElement? FindMember(XDocument xml, string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
                return null;

            return xml.Descendants("member")
                .FirstOrDefault(m => string.Equals((string?)m.Attribute("name"), memberName, StringComparison.Ordinal));
        }

        private static string GetTypeDocName(Type type, bool includeGenericParameters)
        {
            if (type.IsByRef)
                return $"{(type.GetElementType() is { } et ? GetTypeDocName(et, includeGenericParameters) : type.Name)}@";

            if (type.IsPointer)
                return $"{(type.GetElementType() is { } pt ? GetTypeDocName(pt, includeGenericParameters) : type.Name)}*";

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var suffix = type.GetArrayRank() == 1 ? "[]" : $"[{new string(',', type.GetArrayRank() - 1)}]";
                return $"{(elementType is not null ? GetTypeDocName(elementType, includeGenericParameters) : type.Name)}{suffix}";
            }

            if (type.IsGenericParameter)
                return type.DeclaringMethod is null ? $"`{type.GenericParameterPosition}" : $"``{type.GenericParameterPosition}";

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
                var genericTypeName = GetNonGenericTypeName(genericTypeDefinition);

                if (!includeGenericParameters)
                    return genericTypeName;

                var genericArguments = type.GetGenericArguments()
                    .Select(arg => GetTypeDocName(arg, includeGenericParameters: true));

                return $"{genericTypeName}{{{string.Join(",", genericArguments)}}}";
            }

            return GetNonGenericTypeName(type);
        }

        private static string GetNonGenericTypeName(Type type)
        {
            var fullName = (type.FullName ?? type.Name).Replace('+', '.');

            return string.Join('.',
                fullName.Split('.').Select(part =>
                {
                    var tickIndex = part.IndexOf('`');
                    return tickIndex >= 0 ? part[..tickIndex] : part;
                }));
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(' ',
                value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()));
        }
    }
}
