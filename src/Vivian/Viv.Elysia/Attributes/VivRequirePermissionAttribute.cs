using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Viv.Elysia.Interface;
using Viv.Engine;

namespace Viv.Elysia.Attributes
{
    /// <summary>
    /// 按权限编码限制接口访问。编码有两种形态，靠<b>冒号</b>区分：
    ///
    /// <list type="bullet">
    /// <item><description><c>user:add</c> —— 操作权限（对应 <c>AtMenuAction.ActionCode</c>）：
    /// 要求用户的角色里授了这个具体动作。</description></item>
    /// <item><description><c>user</c> —— 页面 / 模块权限（对应 <c>AtMenu.MenuCode</c>）：
    /// 只要有这个菜单（目录就是模块）就能进，不细分动作。</description></item>
    /// </list>
    ///
    /// <para><b>组合语义（两层，别记混）</b></para>
    /// <list type="bullet">
    /// <item><description>同一个特性里多个编码 = <b>任一满足</b>：
    /// <c>[VivRequirePermission("user:add", "user:edit")]</c> —— 能新增或能编辑都放行。</description></item>
    /// <item><description>多个特性叠加 = <b>都要满足</b>：
    /// <c>[VivRequirePermission("user")] [VivRequirePermission("user:add")]</c> ——
    /// 既要有这个菜单、又要有新增动作。</description></item>
    /// </list>
    ///
    /// <para><b>编码是唯一标识，不是 BitIndex</b>：<c>BitIndex</c> 是库里自增值（开发/生产不一致、
    /// 增删会漂移），写进代码必然出事。编码要求全局唯一，两套编码（菜单不带冒号、动作带冒号）
    /// 不能重名。</para>
    ///
    /// <para><b>数据从哪来</b>：<see cref="IUserPermissionChecker"/> 由应用层实现（Apex 走
    /// 用户→角色→掩码 + Redis 缓存）。宿主没注册实现时一律拒绝 —— 启动期
    /// <c>VivGateStartupFilter</c> 会先把这种配置错误挡下来。</para>
    ///
    /// <para><b>不隐式放过超级管理员</b>：这是刻意的。要放行就给超管角色配上对应权限，
    /// 而不是在代码里开一个没人看得见的后门。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class VivRequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        /// <param name="permissions">
        /// 权限编码，至少一个。建议用常量类集中放 —— <c>"user:add"</c> 这种字符串散落在各处，
        /// 拼错只会在运行期表现为「没权限」。
        /// </param>
        public VivRequirePermissionAttribute(params string[] permissions)
        {
            if (permissions is null || permissions.Length == 0)
            {
                throw new ArgumentException("[VivRequirePermission] 至少声明一个权限编码；不限制就别标这个特性。", nameof(permissions));
            }

            foreach (var permission in permissions)
            {
                if (string.IsNullOrWhiteSpace(permission))
                {
                    throw new ArgumentException("[VivRequirePermission] 权限编码不能为空。", nameof(permissions));
                }
            }

            Permissions = permissions.Select(static x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        }

        /// <summary>权限编码集合，任一满足即可（已去重）。</summary>
        public IReadOnlyList<string> Permissions { get; }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // [AllowAnonymous] 优先：授权过滤器不像 AuthorizeFilter 那样认这个标记，得自己看
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                return;
            }

            var user = context.HttpContext.User;
            var userId = user.GetUserId();
            if (userId is null)
            {
                context.Result = VivApiResult.ApiResult(ApiResultCode.TokenEmpty, "登录态无效，请重新登录");
                return;
            }

            var checker = context.HttpContext.RequestServices.GetService<IUserPermissionChecker>();
            if (checker is null)
            {
                // 启动期校验应该已经挡住；真走到这里按拒绝处理（fail-closed），不要静默放行
                context.Result = VivApiResult.ApiResult(ApiResultCode.NoPermission, "权限判定服务未注册");
                return;
            }

            // 任一编码通过即放行；全都不通过才拒绝
            foreach (var permission in Permissions)
            {
                if (await checker
                        .HasPermissionAsync(userId.Value, permission, context.HttpContext.RequestAborted)
                        .ConfigureAwait(false))
                {
                    return;
                }
            }

            context.Result = VivApiResult.ApiResult(ApiResultCode.NoPermission,
                $"无接口操作权限：{string.Join(" 或 ", Permissions)}");
        }
    }
}
