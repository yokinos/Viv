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

        public Task TransformAsync(OpenApiOperation? operation, OpenApiOperationTransformerContext? context, CancellationToken cancellationToken)
        {
            if (operation is null || context?.Description?.ActionDescriptor is not ControllerActionDescriptor cad)
                return Task.CompletedTask;

            var xml = LoadXml(cad.ControllerTypeInfo.Assembly);
            if (xml is null)
                return Task.CompletedTask;

            var member = FindMember(xml, cad.MethodInfo);
            if (member is null)
                return Task.CompletedTask;

            ApplySummary(operation, member);
            ApplyRemarks(operation, member);
            ApplyParameters(operation, member);
            ApplyReturns(operation, member);
            ApplyResponses(operation, member);

            return Task.CompletedTask;
        }

        private static void ApplySummary(OpenApiOperation operation, XElement member)
        {
            var summary = Normalize(member.Element("summary")?.Value);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                operation.Summary = summary;
            }
        }

        private static void ApplyRemarks(OpenApiOperation operation, XElement member)
        {
            var remarks = Normalize(member.Element("remarks")?.Value);
            if (string.IsNullOrWhiteSpace(remarks))
            {
                return;
            }

            operation.Description = string.IsNullOrWhiteSpace(operation.Description)
                ? remarks
                : $"{operation.Description}\n\n{remarks}";
        }

        private static void ApplyParameters(OpenApiOperation operation, XElement member)
        {
            var paramDocs = member.Elements("param")
                .Select(x =>
                {
                    var name = (string?)x.Attribute("name");
                    var description = Normalize(x.Value);
                    return new { Name = name, Description = description };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Description))
                .ToDictionary(x => x.Name!, x => x.Description!);

            if (operation.Parameters is null || operation.Parameters.Count == 0)
            {
                return;
            }

            foreach (var parameter in operation.Parameters)
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                if (!paramDocs.TryGetValue(parameter.Name, out var description))
                {
                    continue;
                }

                parameter.Description = string.IsNullOrWhiteSpace(parameter.Description)
                    ? description
                    : parameter.Description;
            }
        }

        private static void ApplyReturns(OpenApiOperation operation, XElement member)
        {
            var returns = Normalize(member.Element("returns")?.Value);
            if (string.IsNullOrWhiteSpace(returns))
            {
                return;
            }

            operation.Responses ??= new OpenApiResponses();

            if (!operation.Responses.TryGetValue("200", out var response) || response is null)
            {
                response = new OpenApiResponse();
                operation.Responses["200"] = response;
            }

            response.Description = string.IsNullOrWhiteSpace(response.Description)
                ? returns
                : response.Description;
        }

        private static void ApplyResponses(OpenApiOperation operation, XElement member)
        {
            operation.Responses ??= new OpenApiResponses();

            foreach (var responseNode in member.Elements("response"))
            {
                var statusCode = (string?)responseNode.Attribute("code");
                if (string.IsNullOrWhiteSpace(statusCode))
                {
                    continue;
                }

                var description = Normalize(responseNode.Value);
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                if (!operation.Responses.TryGetValue(statusCode, out var response) || response is null)
                {
                    response = new OpenApiResponse();
                    operation.Responses[statusCode] = response;
                }

                response.Description = string.IsNullOrWhiteSpace(response.Description)
                    ? description
                    : response.Description;
            }
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
            var memberName = GetMemberName(method);
            if (string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            return xml.Descendants("member").FirstOrDefault(m =>
            {
                var name = (string?)m.Attribute("name");
                return string.Equals(name, memberName, StringComparison.Ordinal);
            });
        }

        private static string? GetMemberName(MethodInfo method)
        {
            var declaringType = method.DeclaringType;
            if (declaringType is null)
            {
                return null;
            }

            var typeName = GetTypeDocName(declaringType, includeGenericParameters: false);
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            var memberName = $"M:{typeName}.{method.Name}";

            if (method.IsGenericMethod)
            {
                memberName += $"``{method.GetGenericArguments().Length}";
            }

            var parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                memberName += "(" + string.Join(",", parameters.Select(GetParameterTypeDocName)) + ")";
            }

            return memberName;
        }

        private static string GetParameterTypeDocName(ParameterInfo parameter)
        {
            var type = parameter.ParameterType;
            var isByRef = type.IsByRef;

            if (isByRef)
            {
                type = type.GetElementType() ?? type;
            }

            var typeName = GetTypeDocName(type);
            return isByRef ? $"{typeName}@" : typeName;
        }

        private static string GetTypeDocName(Type type, bool includeGenericParameters = true)
        {
            if (type.IsByRef)
            {
                var elementType = type.GetElementType();
                return elementType is null ? type.Name : $"{GetTypeDocName(elementType, includeGenericParameters)}@";
            }

            if (type.IsPointer)
            {
                var elementType = type.GetElementType();
                return elementType is null ? type.Name : $"{GetTypeDocName(elementType, includeGenericParameters)}*";
            }

            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                var rank = type.GetArrayRank();
                var suffix = rank == 1 ? "[]" : $"[{new string(',', rank - 1)}]";
                return elementType is null ? type.Name : $"{GetTypeDocName(elementType, includeGenericParameters)}{suffix}";
            }

            if (type.IsGenericParameter)
            {
                return type.DeclaringMethod is null
                    ? $"`{type.GenericParameterPosition}"
                    : $"``{type.GenericParameterPosition}";
            }

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
                var genericTypeName = GetNonGenericTypeName(genericTypeDefinition);

                if (!includeGenericParameters)
                {
                    return genericTypeName;
                }

                var genericArguments = type.GetGenericArguments()
                    .Select(arg => GetTypeDocName(arg, includeGenericParameters: true));

                return $"{genericTypeName}{{{string.Join(",", genericArguments)}}}";
            }

            return GetNonGenericTypeName(type);
        }

        private static string GetNonGenericTypeName(Type type)
        {
            var fullName = type.FullName ?? type.Name;
            fullName = fullName.Replace('+', '.');

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
            {
                return string.Empty;
            }

            return string.Join(' ',
                value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => x.Trim()));
        }
    }
}
