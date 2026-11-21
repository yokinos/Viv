using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Elysia.Attributes
{
    /// <summary>
    /// 按用户类型限制接口访问。标在 Controller 上限制整个控制器，标在 action 上只限制那一个 ——
    /// 方法级优先于类级，两者都没有就不限制（opt-in，与现有接口行为兼容）。
    /// <para><b>拒绝形状</b>：HTTP 200 + 业务信封码，与 <c>VivContextMiddleware</c> 里 TokenEmpty
    /// 那条路径一致；要改成 401/403 只需设 <c>Response.StatusCode</c>（白名单里已有）。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class VivRequireUserTypeAttribute : Attribute, IAsyncAuthorizationFilter
    {
        /// <param name="userTypes">允许访问的用户类型，至少一个。非法值在启动期就炸
        /// （由 <see cref="Filter.UserTypeGateStartupFilter"/> 触发构造）。</param>
        public VivRequireUserTypeAttribute(params EmUserType[] userTypes)
        {
            if (userTypes is null || userTypes.Length == 0)
            {
                throw new ArgumentException("[VivRequireUserType] 至少声明一个允许访问的用户类型；不限制就别标这个特性。", nameof(userTypes));
            }

            foreach (var userType in userTypes)
            {
                if (!Enum.IsDefined(userType))
                {
                    throw new ArgumentException($"[VivRequireUserType] {(int)userType} 不是 EmUserType 的已定义值。", nameof(userTypes));
                }
            }

            UserTypes = userTypes.Distinct().ToArray();
        }

        /// <summary>允许访问的用户类型集合（已去重）。</summary>
        public IReadOnlyList<EmUserType> UserTypes { get; }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // [AllowAnonymous] 优先：授权过滤器不像 AuthorizeFilter 那样认这个标记，得自己看，
            // 否则登录、健康检查、支付回调这类匿名接口会被一起挡掉。
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            var actual = context.HttpContext.User.GetUserType();
            if (actual is null)
            {
                // 没有 JWT / claim 缺席 / claim 不是整数，三种情况都当「不知道」。
                // 绝不能 default 成 0 —— 0 是运营平台用户，猜错就是提权。
                context.Result = VivApiResult.ApiResult(ApiResultCode.TokenEmpty, "登录态未携带用户类型，请重新登录");
                return Task.CompletedTask;
            }

            if (!UserTypes.Any(userType => (int)userType == actual.Value))
            {
                var allowed = string.Join("/", UserTypes);
                context.Result = VivApiResult.ApiResult(ApiResultCode.NoPermission, $"该接口仅对 {allowed} 用户开放");
            }

            return Task.CompletedTask;
        }
    }
}
