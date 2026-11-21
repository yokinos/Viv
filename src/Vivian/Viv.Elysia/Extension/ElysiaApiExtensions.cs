using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Filter;

namespace Viv.Elysia.Extension
{
    public static class ElysiaApiExtensions
    {
        /// <summary>
        /// 添加自定义的过滤器
        /// </summary>
        /// <param name="filters"></param>
        /// <returns></returns>
        public static FilterCollection AddElysiaFilter(this FilterCollection filters)
        {
            filters.Add<RequestFilterAttribute>();
            filters.Add<OperationLogFilterAttribute>();
            return filters;
        }

        /// <summary>
        /// 启动期校验访问控制闸门，三类问题直接启动失败：
        /// 特性参数写错（空列表 / 未定义的枚举值 / 空动作编码）、
        /// 有 <c>[VivRequirePermission]</c> 却没注册 <c>IUserPermissionChecker</c>、
        /// 特性所在 action 解析不出来。
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
