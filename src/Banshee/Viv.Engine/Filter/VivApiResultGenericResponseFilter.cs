using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Engine.Filter
{
    public class VivApiResultGenericResponseFilter : IOperationAsyncFilter
    {
        public Task ApplyAsync(OpenApiOperation operation, OperationFilterContext context, CancellationToken cancellationToken)
        {
            var methodInfo = context.MethodInfo;
            var returnType = methodInfo.ReturnType;

            if (!typeof(IActionResult).IsAssignableFrom(returnType))
            {
                return Task.CompletedTask;
            }

            var targetType = GetSuccessResponseType(methodInfo, returnType);
            if (targetType == null)
            {
                return Task.CompletedTask;
            }

            ApplySchemaToStatusCode(operation, context, targetType, "200");
            ApplySchemaToStatusCode(operation, context, targetType, "201");

            return Task.CompletedTask;
        }

        private static Type? GetSuccessResponseType(MethodInfo methodInfo, Type returnType)
        {
            var successResponse = methodInfo
                .GetCustomAttributes<ProducesResponseTypeAttribute>(true)
                .FirstOrDefault(attribute =>
                    (attribute.StatusCode == 200 || attribute.StatusCode == 201) &&
                    attribute.Type != null &&
                    attribute.Type != typeof(void));

            if (successResponse?.Type != null)
            {
                return successResponse.Type;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ActionResult<>))
            {
                return returnType.GetGenericArguments()[0];
            }

            return null;
        }

        private static void ApplySchemaToStatusCode(OpenApiOperation operation, OperationFilterContext context, Type targetType, string statusCode)
        {
            if (!operation.Responses.TryGetValue(statusCode, out var response))
            {
                return;
            }

            var schema = context.SchemaGenerator.GenerateSchema(targetType, context.SchemaRepository);

            response.Content.Clear();
            response.Content["application/json"] = new OpenApiMediaType
            {
                Schema = schema
            };
        }
    }
}
