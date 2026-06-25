using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;

namespace Viv.Engine.VivOpenApi
{
    public sealed class VivOpenApiSchemaTransformer : IOpenApiSchemaTransformer
    {
        private readonly ILogger<VivOpenApiSchemaTransformer> _logger;
        private readonly ConcurrentDictionary<Assembly, XDocument?> _xmlCache = new();

        public VivOpenApiSchemaTransformer(ILogger<VivOpenApiSchemaTransformer> logger)
        {
            _logger = logger;
        }

        public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
        {
            if (context?.JsonTypeInfo is null || schema is null)
                return Task.CompletedTask;

            var targetType = context.JsonTypeInfo.Type;
            if (targetType is null)
                return Task.CompletedTask;

            var relatedTypes = CollectRelatedTypes(targetType).ToList();
            var relatedAssemblies = relatedTypes.Select(t => t.Assembly).Distinct().ToList();

            foreach (var asm in relatedAssemblies)
            {
                var xmlDoc = LoadAssemblyXml(asm);
                if (xmlDoc is null)
                    continue;

                FillTypeSummary(schema, xmlDoc, targetType);
                FillPropertySummaries(schema, context.JsonTypeInfo, xmlDoc);
            }

            return Task.CompletedTask;
        }

        private static IEnumerable<Type> CollectRelatedTypes(Type rootType)
        {
            var visited = new HashSet<Type>();
            var queue = new Queue<Type>();
            queue.Enqueue(rootType);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                yield return current;

                if (current.BaseType is not null)
                    queue.Enqueue(current.BaseType);

                foreach (var iface in current.GetInterfaces())
                    queue.Enqueue(iface);

                if (current.IsGenericType)
                {
                    foreach (var arg in current.GetGenericArguments())
                        queue.Enqueue(arg);
                }
            }
        }

        private XDocument? LoadAssemblyXml(Assembly assembly)
        {
            return _xmlCache.GetOrAdd(assembly, asm =>
            {
                try
                {
                    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.xml");
                    if (!File.Exists(xmlFile))
                    {
                        _logger.LogDebug("XML not found: {XmlFile}", xmlFile);
                        return null;
                    }

                    return XDocument.Load(xmlFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load XML for assembly {Assembly}", asm.GetName().Name);
                    return null;
                }
            });
        }

        private void FillTypeSummary(OpenApiSchema schema, XDocument xml, Type type)
        {
            var member = FindXmlMember(xml, $"T:{GetXmlTypeName(type, includeGenericParams: false)}");
            if (member is null)
                return;

            var desc = CombineDesc(
                NormalizeText(member.Element("summary")?.Value),
                NormalizeText(member.Element("remarks")?.Value));

            if (!string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(schema.Description))
            {
                schema.Description = desc;
            }
        }

        private void FillPropertySummaries(OpenApiSchema schema, JsonTypeInfo typeInfo, XDocument xml)
        {
            if (schema.Properties is not { Count: > 0 })
                return;

            var allProps = typeInfo.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var jsonProp in typeInfo.Properties)
            {
                PropertyInfo? propInfo = null;

                if (jsonProp?.AttributeProvider is PropertyInfo p)
                {
                    propInfo = p;
                }
                else if (jsonProp?.Name is not null)
                {
                    propInfo = allProps.FirstOrDefault(p => p.Name == jsonProp.Name) ??
                               allProps.FirstOrDefault(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == jsonProp.Name) ??
                               allProps.FirstOrDefault(p => string.Equals(p.Name, jsonProp.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (propInfo is null)
                    continue;

                var propDesc = GetPropertyDesc(xml, propInfo);
                if (string.IsNullOrWhiteSpace(propDesc))
                    continue;

                foreach (var name in GetCandidateNames(propInfo))
                {
                    if (schema.Properties.TryGetValue(name, out var propSchema) &&
                        string.IsNullOrWhiteSpace(propSchema.Description))
                    {
                        propSchema.Description = propDesc;
                        break;
                    }
                }
            }
        }

        private static IEnumerable<string> GetCandidateNames(PropertyInfo prop)
        {
            yield return prop.Name;

            if (!string.IsNullOrWhiteSpace(prop.Name) && prop.Name.Length > 1)
            {
                yield return char.ToLowerInvariant(prop.Name[0]) + prop.Name[1..];
            }

            var stj = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            if (!string.IsNullOrWhiteSpace(stj))
                yield return stj!;

            var newton = prop.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>()?.PropertyName;
            if (!string.IsNullOrWhiteSpace(newton))
                yield return newton!;
        }

        private string? GetPropertyDesc(XDocument xml, PropertyInfo prop)
        {
            var declaringType = prop.DeclaringType;
            if (declaringType is null)
                return null;

            var member = FindXmlMember(xml, $"P:{GetXmlTypeName(declaringType, false)}.{prop.Name}");
            if (member is null)
                return null;

            return CombineDesc(
                NormalizeText(member.Element("summary")?.Value),
                NormalizeText(member.Element("remarks")?.Value));
        }

        private static XElement? FindXmlMember(XDocument xml, string memberName)
        {
            return xml.Descendants("member")
                .FirstOrDefault(m => string.Equals((string?)m.Attribute("name"), memberName, StringComparison.Ordinal));
        }

        private static string CombineDesc(string? summary, string? remarks)
        {
            if (string.IsNullOrWhiteSpace(summary)) return remarks ?? string.Empty;
            if (string.IsNullOrWhiteSpace(remarks)) return summary;
            return $"{summary}\n\n{remarks}";
        }

        private static string NormalizeText(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            return string.Join(' ',
                raw.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(x => x.Trim()));
        }

        private static string GetXmlTypeName(Type type, bool includeGenericParams)
        {
            if (type.IsByRef)
                return $"{(type.GetElementType() is { } et ? GetXmlTypeName(et, includeGenericParams) : type.Name)}@";

            if (type.IsPointer)
                return $"{(type.GetElementType() is { } pt ? GetXmlTypeName(pt, includeGenericParams) : type.Name)}*";

            if (type.IsArray)
            {
                var elemType = type.GetElementType();
                var suffix = type.GetArrayRank() == 1 ? "[]" : $"[{new string(',', type.GetArrayRank() - 1)}]";
                return $"{(elemType is not null ? GetXmlTypeName(elemType, includeGenericParams) : type.Name)}{suffix}";
            }

            if (type.IsGenericParameter)
                return type.DeclaringMethod is null ? $"`{type.GenericParameterPosition}" : $"``{type.GenericParameterPosition}";

            if (type.IsGenericType)
            {
                var genDef = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
                var ns = genDef.Namespace ?? "";
                var baseName = genDef.Name;
                var fullBaseName = string.IsNullOrEmpty(ns) ? baseName : $"{ns}.{baseName}";

                if (!includeGenericParams)
                    return fullBaseName;

                var args = string.Join(",", type.GetGenericArguments().Select(x => GetXmlTypeName(x, true)));
                return $"{fullBaseName}{{{args}}}";
            }

            var full = (type.FullName ?? type.Name).Replace('+', '.');
            return string.Join('.',
                full.Split('.').Select(seg =>
                {
                    var tick = seg.IndexOf('`');
                    return tick >= 0 ? seg[..tick] : seg;
                }));
        }
    }
}