using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Viv.Elysia.Attributes;
using Viv.Engine;
using Viv.Entity.Enums;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// 按用户类型限制接口访问。类型只从 JWT claim 读 —— 这里用 <see cref="ClaimsPrincipal"/> 摆身份，
    /// 不涉及 <c>IVivContext</c>、也不涉及任何 <c>x-viv-*</c> 头（框架层不携带业务分类）。
    /// </summary>
    public class VivRequireUserTypeTests
    {
        #region 被测类型

        [VivRequireUserType(EmUserType.OrgUser, EmUserType.CompanyUser)]
        private sealed class OrgOrCompanyController
        {
            public Task AnyAsync() => Task.CompletedTask;
        }

        [VivRequireUserType(EmUserType.OrgUser)]
        private sealed class OrgController
        {
            public Task AnyAsync() => Task.CompletedTask;

            [VivRequireUserType(EmUserType.CompanyUser)]
            public Task CompanyOnlyAsync() => Task.CompletedTask;
        }

        #endregion

        private static async Task<AuthorizationFilterContext> Run(
            Type controller,
            string methodName,
            string? userTypeClaim = null,
            bool authenticated = true,
            bool allowAnonymous = false)
        {
            var method = controller.GetMethod(methodName)!;
            var descriptor = new ControllerActionDescriptor
            {
                ControllerTypeInfo = controller.GetTypeInfo(),
                MethodInfo = method,
                EndpointMetadata = allowAnonymous ? [new AllowAnonymousAttribute()] : []
            };

            var claims = userTypeClaim is null
                ? Array.Empty<Claim>()
                : [new Claim(VivUserClaimTypes.UserType, userTypeClaim)];

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(authenticated ? claims : Array.Empty<Claim>(),
                    authenticated ? "test" : null))
            };

            var context = new AuthorizationFilterContext(
                new ActionContext(httpContext, new RouteData(), descriptor), new List<IFilterMetadata>());

            await ResolveAttribute(controller, method).OnAuthorizationAsync(context);
            return context;
        }

        private static VivRequireUserTypeAttribute ResolveAttribute(Type controller, MethodInfo method)
            => method.GetCustomAttribute<VivRequireUserTypeAttribute>()
               ?? controller.GetCustomAttribute<VivRequireUserTypeAttribute>()!;

        [Fact]
        public async Task 类型匹配_放行()
        {
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync),
                userTypeClaim: ((int)EmUserType.CompanyUser).ToString());

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task 类型不匹配_无权限()
        {
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync),
                userTypeClaim: ((int)EmUserType.TenantUser).ToString());

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task claim缺席_按登录态不完整拒绝()
        {
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync));

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.TokenEmpty, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 未认证_拒绝()
        {
            // 没配 TokenOption 的宿主、或服务间调用不带 JWT —— 一律拒绝（fail-closed）
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync),
                userTypeClaim: ((int)EmUserType.OrgUser).ToString(), authenticated: false);

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.TokenEmpty, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task claim不是数字_拒绝()
        {
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync),
                userTypeClaim: "org");

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.TokenEmpty, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 用户类型为0运营平台_不等于缺席()
        {
            // Master = 0 是合法值：没声明 Master 的接口要拒绝，但不能因为「值是 0」被当成没登录
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync),
                userTypeClaim: ((int)EmUserType.Master).ToString());

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 匿名接口_优先放行()
        {
            // 授权过滤器不像 AuthorizeFilter 那样认 [AllowAnonymous]，必须自己看
            var context = await Run(typeof(OrgOrCompanyController), nameof(OrgOrCompanyController.AnyAsync),
                userTypeClaim: ((int)EmUserType.TenantUser).ToString(), allowAnonymous: true);

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task 方法级优先于类级()
        {
            // 类级只放 OrgUser，方法级改成 CompanyUser —— 运行期以方法级为准
            var context = await Run(typeof(OrgController), nameof(OrgController.CompanyOnlyAsync),
                userTypeClaim: ((int)EmUserType.CompanyUser).ToString());

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task 方法无标记_用类级()
        {
            var context = await Run(typeof(OrgController), nameof(OrgController.AnyAsync),
                userTypeClaim: ((int)EmUserType.CompanyUser).ToString());

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public void 空类型列表_构造即失败()
        {
            Assert.Throws<ArgumentException>(() => new VivRequireUserTypeAttribute());
        }

        [Fact]
        public void 未定义的枚举值_构造即失败()
        {
            Assert.Throws<ArgumentException>(() => new VivRequireUserTypeAttribute((EmUserType)99));
        }
    }
}
