using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Engine.Options;

namespace Viv.Engine
{
    public static class VivEngineExtensions
    {
        private const string AjaxHeaderName = "X-Requested-With";
        private const string AjaxHeaderValue = "XMLHttpRequest";

        /// <summary>
        /// 注册Viv相关服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static IServiceCollection AddViv(this IServiceCollection services, VivOptions vivOptions)
        {
            ArgumentNullException.ThrowIfNull(vivOptions);
            VivRegister.Register(services, vivOptions);
            return services;
        }

        public static void VivAutofacRegister(this ContainerBuilder builder, DIOptions diOptions, Action<ContainerBuilder>? customSet = default)
        {
            // 自动依赖注入
            AutoDependencyRegister(builder);

            // 自定义的注入
            customSet?.Invoke(builder);

            // 可能不需要抽象
            if (diOptions == null) return;

            var serviceImplTypes = TypeScanMagic.Scan(diOptions.ServiceImplementation);
            if (!serviceImplTypes.IsNullOrEmpty())
            {

                builder.RegisterTypes(serviceImplTypes.ToArray())
                       .AsImplementedInterfaces()
                       .InstancePerLifetimeScope();
            }

            var repoImplTypes = TypeScanMagic.Scan(diOptions.RepositoryImplementation);
            if (!repoImplTypes.IsNullOrEmpty())
            {
                builder.RegisterTypes(repoImplTypes.ToArray())
                   .AsImplementedInterfaces()
                   .InstancePerLifetimeScope();
            }
        }

        /// <summary>
        /// 自动依赖注入
        /// </summary>
        /// <param name="builder"></param>
        private static void AutoDependencyRegister(ContainerBuilder builder)
        {
            var typeList = TypeScanMagic.ScanTypes<IDependency>();
            if (typeList.IsNullOrEmpty())
                return;

            foreach (var implementationType in typeList)
            {
                var dependencyAttribute = implementationType.GetCustomAttributes(typeof(VivDependencyAttribute), true).OfType<VivDependencyAttribute>().SingleOrDefault();

                var lifetime = dependencyAttribute?.Lifetime ?? DependencyLifetime.Scoped;
                var asSelf = dependencyAttribute?.AsSelf ?? false;
                var registration = builder.RegisterType(implementationType);
                var tag = dependencyAttribute?.Tag;

                if (asSelf)
                {
                    registration = registration.AsSelf();
                }
                else
                {
                    var interfaces = implementationType.GetInterfaces().Where(x => x != typeof(IDependency)).ToArray();
                    if (interfaces.IsNullOrEmpty())
                    {
                        registration = registration.AsSelf();
                        continue;
                    }

                    if (tag != null)
                    {
                        foreach (var contract in interfaces)
                        {
                            registration = registration.Keyed(tag, contract);
                        }
                    }
                    else
                    {
                        registration = registration.AsImplementedInterfaces();
                    }
                }

                registration = lifetime switch
                {
                    DependencyLifetime.Singleton => registration.SingleInstance(),
                    DependencyLifetime.Transient => registration.InstancePerDependency(),
                    DependencyLifetime.Scoped => registration.InstancePerLifetimeScope(),
                    _ => registration.InstancePerLifetimeScope()
                };

                registration.PreserveExistingDefaults();
            }
        }

        /// <summary>
        /// 判断是否为Ajax请求
        /// </summary>
        /// <summary>
        /// 判断当前请求是否为 Ajax 或接口请求。
        ///
        /// 判断条件：
        /// 1. X-Requested-With 为 XMLHttpRequest；
        /// 2. Accept 包含 application/json；
        /// 3. 请求路径匹配指定规则；
        /// 4. 可选：将 POST 请求视为 Ajax。
        /// </summary>
        /// <param name="request">当前请求</param>
        /// <param name="rule">路径匹配规则，例如：/api/</param>
        /// <param name="includePost">
        /// 是否将 POST 请求也视为 Ajax。
        /// 默认 false，因为普通表单提交也可能是 POST。
        /// </param>
        public static bool IsAjax(this HttpRequest request, string rule = "", bool includePost = false)
        {
            if (request == null)
            {
                return false;
            }

            if (IsAjaxHeader(request))
            {
                return true;
            }

            if (AcceptsJson(request))
            {
                return true;
            }

            if (IsMatchedPath(request, rule))
            {
                return true;
            }

            if (includePost && HttpMethods.IsPost(request.Method))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断是否包含 Ajax 请求头。
        /// </summary>
        private static bool IsAjaxHeader(HttpRequest request)
        {
            if (!request.Headers.TryGetValue(AjaxHeaderName, out var headerValue))
            {
                return false;
            }

            return headerValue.Any(value => string.Equals(value?.Trim(), AjaxHeaderValue, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 判断客户端是否期望 JSON 响应。
        /// </summary>
        private static bool AcceptsJson(HttpRequest request)
        {
            if (!request.Headers.TryGetValue("Accept", out var acceptValues))
            {
                return false;
            }

            return acceptValues
                .SelectMany(value => value?.Split(',') ?? [])
                .Select(value => value.Split(';')[0].Trim())
                .Any(value =>
                    string.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "text/json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "application/problem+json", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 判断请求路径是否匹配规则。
        /// </summary>
        private static bool IsMatchedPath(HttpRequest request, string rule)
        {
            if (string.IsNullOrWhiteSpace(rule))
            {
                return false;
            }

            var path = request.Path.Value;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            rule = rule.Trim();
            return path.Contains(rule, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取请求头中的Token信息
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static string GetJwtToken(this HttpContext context)
        {
            return context.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        }

        /// <summary>
        /// [扩展方法] 设置响应信息
        /// </summary>
        /// <param name="context"></param>
        /// <param name="code"></param>
        /// <returns></returns>
        public static async Task SetApiResponseAsync(this HttpContext context, ApiResultCode code, int? httpStatusCode = null)
        {
            var result = VivApiResult.ApiRsult(code);
            result.RequestId = context.TraceIdentifier;
            context.Response.Clear();
            httpStatusCode ??= context.Response.StatusCode;
            // 与 VivApiResult.ExecuteResultAsync 保持一致：仅 VivRunDefine 白名单内的状态码原样返回，其余强制 200
            context.Response.StatusCode = VivRunDefine.AllowedHttpStatusCodes.Contains(httpStatusCode.Value)
                ? httpStatusCode.Value
                : 200;
            context.Response.ContentType = "application/json;charset=UTF-8";
            await context.Response.WriteAsync(result.ToJson(JsonNetSetting.ApiResponseSettings), Encoding.UTF8);
        }
    }
}
