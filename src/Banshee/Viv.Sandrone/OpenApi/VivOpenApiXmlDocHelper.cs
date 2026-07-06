using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Xml.Linq;

namespace Viv.Sandrone.OpenApi;

internal static class VivOpenApiXmlDocHelper
{
    private static readonly ConcurrentDictionary<Assembly, XDocument?> XmlCache = new();

    public static XDocument? LoadAssemblyXml(Assembly assembly)
    {
        return XmlCache.GetOrAdd(assembly, asm =>
        {
            try
            {
                var candidates = new List<string>();

                if (!string.IsNullOrWhiteSpace(asm.Location))
                    candidates.Add(Path.ChangeExtension(asm.Location, ".xml"));

                candidates.Add(Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.xml"));

                foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (File.Exists(path))
                        return XDocument.Load(path);
                }
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        });
    }

    public static XElement? FindXmlMember(XDocument xml, string memberName)
    {
        return xml.Descendants("member")
            .FirstOrDefault(m => string.Equals((string?)m.Attribute("name"), memberName, StringComparison.Ordinal));
    }

    public static string CombineDesc(string? summary, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(summary)) return remarks ?? string.Empty;
        if (string.IsNullOrWhiteSpace(remarks)) return summary;
        return $"{summary}\n\n{remarks}";
    }

    public static string NormalizeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return string.Join(' ',
            raw.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(x => x.Trim()));
    }

    public static string GetXmlTypeName(Type type, bool includeGenericParams)
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
