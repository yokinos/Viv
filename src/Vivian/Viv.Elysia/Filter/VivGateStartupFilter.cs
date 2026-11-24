using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using Viv.Elysia.Attributes;
using Viv.Elysia.Interface;
using Viv.Elysia.Logging;

namespace Viv.Elysia.Filter
{
    /// <summary>
    /// 访问控制闸门与操作日志的启动期校验，三件事：
    ///
    /// <para>① 把每个 <c>[VivRequireUserType]</c> / <c>[VivRequirePermission]</c> 解析一遍。
    /// 特性构造器里有参数校验（空列表、未定义枚举值、空编码），但那段代码只有真正构造特性时才跑；
    /// MVC 是懒构建 action 描述的，不主动触发就要等第一个请求才炸 —— 那时错误码是 500，现场看不出是哪条特性。</para>
    ///
    /// <para>② 有 <c>[VivRequirePermission]</c> 却没注册 <see cref="IUserPermissionChecker"/> 时直接启动失败。
    /// 这是最隐蔽的一种配置错误：服务能起来、鉴权也正常，就是带特性的接口永远 -601。
    /// 排查时会先怀疑角色没配、权限点不对，最后才怀疑「实现根本没装」。</para>
    ///
    /// <para>③ 有 <c>[OperationLog]</c> 却没有任何 <see cref="IOperationLogWriter"/> 时直接启动失败。
    /// 同样是「能起来但静默失效」：接口标了审计、请求也正常，日志一条没写，只有运行期的 Warning 在喊。
    /// 绝大多数情况是忘了调 <c>AddVivOperationLog()</c>。</para>
    ///
    /// <para>拦截本身由特性自己完成（都实现了 <c>IAsyncAuthorizationFilter</c>），
    /// 所以不注册本校验时限制依然生效，只是配置错误要等首个请求才暴露。</para>
    /// </summary>
    internal sealed class VivGateStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var needsPermissionChecker = false;
            var needsOperationLogWriter = false;

            var actions = app.ApplicationServices
                .GetRequiredService<IActionDescriptorCollectionProvider>()
                .ActionDescriptors
                .Items
                .OfType<ControllerActionDescriptor>();

            foreach (var action in actions)
            {
                // 读一次就会触发构造器校验；方法级优先、没有再看控制器
                _ = action.MethodInfo.GetCustomAttribute<VivRequireUserTypeAttribute>(inherit: true)
                    ?? action.ControllerTypeInfo.GetCustomAttribute<VivRequireUserTypeAttribute>(inherit: true);

                var permissions = action.MethodInfo.GetCustomAttributes<VivRequirePermissionAttribute>(inherit: true).ToArray();
                if (permissions.Length == 0)
                {
                    permissions = action.ControllerTypeInfo.GetCustomAttributes<VivRequirePermissionAttribute>(inherit: true).ToArray();
                }

                if (permissions.Length > 0)
                {
                    needsPermissionChecker = true;
                }

                // 读一次同样会触发 OperationLogAttribute 构造器里的枚举值校验
                if (action.MethodInfo.GetCustomAttribute<OperationLogAttribute>(inherit: true) is not null)
                {
                    needsOperationLogWriter = true;
                }
            }

            if (needsPermissionChecker && !IsServiceRegistered(app.ApplicationServices, typeof(IUserPermissionChecker)))
            {
                throw new InvalidOperationException(
                    "检测到 [VivRequirePermission]，但容器里没有 IUserPermissionChecker 的实现 —— " +
                    "所有带该特性的接口都会返回 -601。请在业务程序集里实现它" +
                    "（Apex 已有 UserPermissionRepository），并确认它落在 DIOption 的 RepositoryImplementation 扫描范围内。");
            }

            if (needsOperationLogWriter && !IsServiceRegistered(app.ApplicationServices, typeof(IOperationLogWriter)))
            {
                throw new InvalidOperationException(
                    "检测到 [OperationLog]，但容器里没有任何 IOperationLogWriter —— 这些接口的审计会静默丢失。" +
                    "请调用 builder.Services.AddVivOperationLog()（走消息队列），" +
                    "或自己实现 IOperationLogWriter 后注册进容器。");
            }

            next(app);
        };

        /// <summary>
        /// 只看注册表、不实例化。用 <see cref="IServiceProviderIsService"/> 是因为
        /// <see cref="IUserPermissionChecker"/> 的实现是 Scoped（依赖 DbContext），
        /// 从根容器 GetService 会在开启作用域校验的宿主上抛异常。
        /// </summary>
        private static bool IsServiceRegistered(IServiceProvider services, Type serviceType)
            => services.GetRequiredService<IServiceProviderIsService>().IsService(serviceType);
    }
}
