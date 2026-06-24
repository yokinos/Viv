using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Collections.Concurrent;
using System.Reflection;
using System.Xml.Linq;

namespace Viv.Engine
{
    public sealed class VivOpenApiOperationTransformer : IOpenApiOperationTransformer
    {
        private static readonly ConcurrentDictionary<Assembly, XDocument?> XmlCache = new();

        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.ActionDescriptor is not ControllerActionDescriptor cad)
                return Task.CompletedTask;

            var xml = LoadXml(cad.ControllerTypeInfo.Assembly);
            if (xml is null)
                return Task.CompletedTask;

            var member = FindMember(xml, cad.MethodInfo);
            if (member is null)
                return Task.CompletedTask;

            var summary = Normalize(member.Element("summary")?.Value);
            if (!string.IsNullOrWhiteSpace(summary))
                operation.Summary = summary;

            var remarks = Normalize(member.Element("remarks")?.Value);
            if (!string.IsNullOrWhiteSpace(remarks))
            {
                operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                    ? remarks
                    : $"{operation.Description}\n\n{remarks}";
            }

            var paramDocs = member.Elements("param")
                .Where(x => x.Attribute("name") is not null)
                .ToDictionary(x => (string)x.Attribute("name")!, x => Normalize(x.Value));

            if (operation.Parameters is not null)
            {
                foreach (var parameter in operation.Parameters)
                {
                    if (parameter.Name is not null &&
                        paramDocs.TryGetValue(parameter.Name, out var description) &&
                        !string.IsNullOrWhiteSpace(description))
                    {
                        parameter.Description = string.IsNullOrWhiteSpace(parameter.Description)
                            ? description
                            : parameter.Description;
                    }
                }
            }

            return Task.CompletedTask;
        }

        private static XDocument? LoadXml(Assembly assembly)
        {
            return XmlCache.GetOrAdd(assembly, asm =>
            {
                var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.xml");
                return File.Exists(xmlPath) ? XDocument.Load(xmlPath) : null;
            });
        }

        private static XElement? FindMember(XDocument xml, MethodInfo method)
        {
            var declaringType = method.DeclaringType;
            if (declaringType is null)
                return null;

            // 适合常见 controller action；同名重载时按参数个数做一次粗匹配
            var memberPrefix = $"M:{declaringType.FullName}.{method.Name}";
            var paramCount = method.GetParameters().Length;

            return xml.Descendants("member").FirstOrDefault(m =>
            {
                var name = (string?)m.Attribute("name");
                if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(memberPrefix, StringComparison.Ordinal))
                    return false;

                return m.Elements("param").Count() == paramCount;
            });
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