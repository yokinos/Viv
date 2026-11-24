using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using Viv.Elysia.Filter;
using Viv.Elysia.Logging;

namespace Viv.Elysia.Extension
{
    public static class ElysiaApiExtensions
    {
        /// <summary>
        /// 添加自定义的过滤器。
        /// <para><see cref="OperationLogFilter"/> 只是<b>采集器</b>：它挂在这里，但记不记、记到哪儿
        /// 由 <c>[OperationLog]</c> 标注与 <see cref="IOperationLogWriter"/> 决定。没调用
        /// <see cref="AddVivOperationLog"/> 时它什么都不会写（启动期还有一道校验会先挡住）。</para>
        /// </summary>
        public static FilterCollection AddElysiaFilter(this FilterCollection filters)
        {
            filters.Add<RequestFilterAttribute>();
            filters.Add<OperationLogFilter>();
            return filters;
        }

        /// <summary>
        /// 启用操作日志：注册采集策略、业务补充入口，以及默认的「发 MQ 事件」出口。
        ///
        /// <para><b>两种落地方式</b>：</para>
        /// <list type="bullet">
        /// <item><description>照旧走消息队列：只调这个方法即可，<c>EventOperationLogWriter</c> 会把记录
        /// 转成 <c>UserOperationLogEvent</c> 发出去（没配 MQ 时记一条 Warning 就返回）。</description></item>
        /// <item><description>自己落库 / 写文件：实现 <see cref="IOperationLogWriter"/> 用普通 DI 注册
        /// （<c>services.AddScoped&lt;IOperationLogWriter, DbOperationLogWriter&gt;()</c>），
        /// 不想要 MQ 那份就把 <see cref="OperationLogOptions.UseEventPublisher"/> 置 false。</description></item>
        /// </list>
        ///
        /// <para>两个出口并存是允许的：容器里有几个实现就依次执行几个，每个独立兜异常。</para>
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure">
        /// 策略配置。<b>立即执行</b>，不是 MVC 那种延迟 Configure —— 出口要不要注册得在注册期就定下来。
        /// </param>
        public static IServiceCollection AddVivOperationLog(this IServiceCollection services, Action<OperationLogOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            var options = new OperationLogOptions();
            configure?.Invoke(options);

            services.TryAddSingleton(options);
            services.TryAddScoped<IOperationLogScope, OperationLogScope>();
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            if (options.UseEventPublisher)
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IOperationLogWriter, EventOperationLogWriter>());
            }

            return services;
        }

        /// <summary>
        /// 启动期校验访问控制闸门，三类问题直接启动失败：
        /// 特性参数写错（空列表 / 未定义的枚举值 / 空动作编码）、
        /// 有 <c>[VivRequirePermission]</c> 却没注册 <c>IUserPermissionChecker</c>、
        /// 有 <c>[OperationLog]</c> 却没有任何 <c>IOperationLogWriter</c>（多半是忘了 <see cref="AddVivOperationLog"/>）。
        ///
        /// <para>拦截本身由特性自己完成 —— 都实现了 <c>IAsyncAuthorizationFilter</c>，标上就生效，
        /// 不依赖这里注册任何过滤器。所以本方法是<b>可选</b>的，只买「早点炸」这一条；
        /// 但强烈建议接，否则一个编码笔误要等上线后表现为「所有人都没权限」。</para>
        ///
        /// <para>不塞进 <see cref="AddElysiaFilter"/>：那个是操作日志那套，混在一起会让
        /// 「开了操作日志」的服务意外获得这些校验语义。</para>
        /// </summary>
        public static IServiceCollection AddVivApiGates(this IServiceCollection services)
        {
            services.AddTransient<IStartupFilter, VivGateStartupFilter>();
            return services;
        }
    }
}
