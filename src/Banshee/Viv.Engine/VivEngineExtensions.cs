using Autofac;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Attributes;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Engine.Options;
using Viv.Engine.Power;
using Viv.Engine.UnitOfWork;

namespace Viv.Engine
{
    public static class VivEngineExtensions
    {
        /// <summary>
        /// 注册Viv相关服务
        /// </summary>
        /// <param name="services"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static IServiceCollection AddViv(this IServiceCollection services, VivOptions vivOptions)
        {
            ArgumentNullException.ThrowIfNull(vivOptions);
            InternalTrustGuard.Validate(vivOptions);
            VivRegister.Register(services, vivOptions);
            return services;
        }

        public static void VivAutofacRegister(this ContainerBuilder builder, VivOptions vivOptions, Action<ContainerBuilder>? customSet = default)
        {
            ArgumentNullException.ThrowIfNull(vivOptions);

            // 自动依赖注入
            AutoDependencyRegister(builder);

            // 自定义的注入
            customSet?.Invoke(builder);

            // 可能不需要抽象
            var diOptions = vivOptions.DIOption;
            if (diOptions == null) return;

            var serviceImplTypes = TypeScanMagic.Scan(diOptions.ServiceImplementation);
            var repoImplTypes = TypeScanMagic.Scan(diOptions.RepositoryImplementation);

            // 工作单元：先解析出哪些类型要开接口代理，并做启动期校验 ——
            // 拦不住的情况（非 virtual / 同步方法 / 没按接口注册）一律在此硬报错，
            // 绝不让「标了特性但没有事务」这种静默失效漏到运行期
            var intercepted = UnitOfWorkRegistration.Resolve(
                serviceImplTypes.Concat(repoImplTypes),
                TypeScanMagic.ScanTypes<IDependency>(),
                vivOptions.DatabaseOption != null);

            RegisterWorkUnitInterceptors(builder, intercepted);

            RegisterScannedTypes(builder, serviceImplTypes, intercepted);
            RegisterScannedTypes(builder, repoImplTypes, intercepted);
        }

        /// <summary>
        /// 注册扫描到的实现类型。<b>只有带 <c>[VivUnitOfWork]</c> 的类型才开接口代理</b> ——
        /// 没标的一律走原路，零代理开销、零调试干扰。
        /// </summary>
        private static void RegisterScannedTypes(ContainerBuilder builder, List<Type> implTypes, IReadOnlyCollection<Type> intercepted)
        {
            if (implTypes.IsNullOrEmpty()) return;

            var closedTypes = implTypes.Where(t => !t.IsGenericTypeDefinition).ToArray();
            var openGenericTypes = implTypes.Where(t => t.IsGenericTypeDefinition).ToArray();

            if (closedTypes.Length != 0)
            {
                var interceptedSet = new HashSet<Type>(intercepted);
                var plain = closedTypes.Where(t => !interceptedSet.Contains(t)).ToArray();
                var proxied = closedTypes.Where(interceptedSet.Contains).ToArray();

                if (plain.Length != 0)
                {
                    builder.RegisterTypes(plain).AsImplementedInterfaces().InstancePerLifetimeScope();
                }

                if (proxied.Length != 0)
                {
                    builder.RegisterTypes(proxied)
                        .AsImplementedInterfaces()
                        .InstancePerLifetimeScope()
                        .EnableInterfaceInterceptors()
                        .InterceptedBy(typeof(AsyncDeterminationInterceptor));
                }
            }

            foreach (var openGeneric in openGenericTypes)
            {
                builder.RegisterGeneric(openGeneric).AsImplementedInterfaces().InstancePerLifetimeScope();
            }
        }

        /// <summary>
        /// 注册工作单元拦截器。
        ///
        /// <c>InterceptedBy</c> 只认 <c>IInterceptor</c>，而 <c>VivUnitOfWorkInterceptor</c> 继承的
        /// <c>AsyncInterceptorBase</c> 实现的是 <c>IAsyncInterceptor</c> —— 中间必须垫一层
        /// <c>AsyncDeterminationInterceptor</c> 适配器，否则解析代理时抛 <c>InvalidCastException</c>
        /// （已实测）。
        ///
        /// 两者都注册成 <c>InstancePerLifetimeScope</c>：拦截器随作用域走，
        /// 它注入的 <c>IVivUnitOfWork</c> 才是当前请求那个事务。已实测 Autofac 的接口代理
        /// 从<b>当前</b>作用域解析拦截器，不会落到根作用域。
        /// </summary>
        private static void RegisterWorkUnitInterceptors(ContainerBuilder builder, Type[] intercepted)
        {
            if (intercepted.Length == 0) return;

            builder.RegisterType<VivUnitOfWorkInterceptor>().AsSelf().InstancePerLifetimeScope();

            builder.Register(c => new AsyncDeterminationInterceptor(c.Resolve<VivUnitOfWorkInterceptor>()))
                .AsSelf()
                .InstancePerLifetimeScope();
        }

        /// <summary>
        /// 自动依赖注入
        /// </summary>
        /// <param name="builder"></param>
        private static void AutoDependencyRegister(ContainerBuilder builder)
        {
            var typeList = TypeScanMagic.ScanTypes<IDependency>();
            if (typeList.IsNullOrEmpty()) return;

            foreach (var implementationType in typeList)
            {
                var dependencyAttribute = implementationType.GetCustomAttribute<VivDependencyAttribute>();
                var lifetime = dependencyAttribute?.Lifetime ?? DependencyLifetime.Scoped;
                var asSelf = dependencyAttribute?.AsSelf ?? false;
                var tag = dependencyAttribute?.Tag;
                 
                if (implementationType.IsGenericTypeDefinition)
                {
                    var registration = builder.RegisterGeneric(implementationType);

                    if (asSelf)
                        registration = registration.AsSelf();
                    else
                    {
                        var interfaces = implementationType.GetInterfaces().Where(i => i != typeof(IDependency)).ToArray();
                        if (interfaces.Length == 0)
                            registration = registration.AsSelf();
                        else if (tag != null)
                            foreach (var i in interfaces) registration = registration.Keyed(tag, i);
                        else
                            registration = registration.AsImplementedInterfaces();
                    }

                    registration = lifetime switch
                    {
                        DependencyLifetime.Singleton => registration.SingleInstance(),
                        DependencyLifetime.Transient => registration.InstancePerDependency(),
                        _ => registration.InstancePerLifetimeScope()
                    };
                }
                else
                {
                    var registration = builder.RegisterType(implementationType);

                    if (asSelf)
                        registration = registration.AsSelf();
                    else
                    {
                        var interfaces = implementationType.GetInterfaces().Where(i => i != typeof(IDependency)).ToArray();
                        if (interfaces.Length == 0)
                            registration = registration.AsSelf();
                        else if (tag != null)
                            foreach (var i in interfaces) registration = registration.Keyed(tag, i);
                        else
                            registration = registration.AsImplementedInterfaces();
                    }

                    registration = lifetime switch
                    {
                        DependencyLifetime.Singleton => registration.SingleInstance(),
                        DependencyLifetime.Transient => registration.InstancePerDependency(),
                        _ => registration.InstancePerLifetimeScope()
                    };
                }
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
            if (!request.Headers.TryGetValue("X-Requested-With", out var headerValue))
            {
                return false;
            }

            return headerValue.Any(value => string.Equals(value?.Trim(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase));
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
            var result = VivApiResult.ApiResult(code);
            result.TraceId = context.TraceIdentifier;
            context.Items[VivRunDefine.ApiResultItemKey] = result;
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
