using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Viv.Elysia.Attributes;
using Viv.Elysia.Interface;
using Viv.Engine;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// 按「功能 + 操作」限制接口：只认 token 里的用户 Id + <see cref="IUserPermissionChecker"/> 的判定结果。
    /// 多特性叠加 = 都要满足；没注册判定服务 = 一律拒绝（fail-closed）。
    /// </summary>
    public class VivRequirePermissionTests
    {
        #region 被测类型

        private sealed class CreateController
        {
            [VivRequirePermission("user:create")]
            public Task CreateAsync() => Task.CompletedTask;
        }

        private sealed class CreateAndExportController
        {
            [VivRequirePermission("user:create")]
            [VivRequirePermission("user:export")]
            public Task ExportAsync() => Task.CompletedTask;
        }

        /// <summary>同一特性里两个编码 = 任一满足</summary>
        private sealed class CreateOrUpdateController
        {
            [VivRequirePermission("user:create", "user:update")]
            public Task SaveAsync() => Task.CompletedTask;
        }

        /// <summary>模块级编码：只要有这个菜单就能进</summary>
        private sealed class ModuleLevelController
        {
            [VivRequirePermission("user")]
            public Task AnyAsync() => Task.CompletedTask;
        }

        #endregion

        private sealed class FakeChecker : IUserPermissionChecker
        {
            private readonly HashSet<string> _granted;

            public FakeChecker(params string[] granted) => _granted = [.. granted];

            public List<string> Queried { get; } = [];

            public Task<bool> HasPermissionAsync(long userId, string permissionCode, CancellationToken cancellationToken = default)
            {
                Queried.Add(permissionCode);
                return Task.FromResult(_granted.Contains(permissionCode));
            }
        }

        private static async Task<AuthorizationFilterContext> Run(
            Type controller,
            string methodName,
            FakeChecker? checker,
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

            var identity = new ClaimsIdentity(
                authenticated ? [new Claim(ClaimTypes.NameIdentifier, "42")] : Array.Empty<Claim>(),
                authenticated ? "test" : null);

            var services = new ServiceCollection();
            if (checker is not null)
            {
                services.AddSingleton<IUserPermissionChecker>(checker);
            }

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                RequestServices = services.BuildServiceProvider()
            };

            var context = new AuthorizationFilterContext(
                new ActionContext(httpContext, new RouteData(), descriptor), new List<IFilterMetadata>());

            // MVC 会把同一个 action 上叠加的多个授权过滤器都跑一遍 —— 这里照做
            foreach (var attribute in method.GetCustomAttributes<VivRequirePermissionAttribute>())
            {
                await attribute.OnAuthorizationAsync(context);
            }

            return context;
        }

        [Fact]
        public async Task 有权限_放行()
        {
            var context = await Run(typeof(CreateController), nameof(CreateController.CreateAsync),
                new FakeChecker("user:create"));

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task 无权限_拒绝()
        {
            var context = await Run(typeof(CreateController), nameof(CreateController.CreateAsync),
                new FakeChecker("user:export"));

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 一个特性里的多个编码_任一满足即可()
        {
            // 只授了 update 也得放行：多编码是 OR，不是 AND
            var checker = new FakeChecker("user:update");
            var context = await Run(typeof(CreateOrUpdateController), nameof(CreateOrUpdateController.SaveAsync), checker);

            Assert.Null(context.Result);
        }

        [Fact]
        public async Task 一个特性里的多个编码_全都不满足才拒绝()
        {
            var context = await Run(typeof(CreateOrUpdateController), nameof(CreateOrUpdateController.SaveAsync),
                new FakeChecker("user:delete"));

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 模块级编码_按菜单权限判()
        {
            var granted = await Run(typeof(ModuleLevelController), nameof(ModuleLevelController.AnyAsync),
                new FakeChecker("user"));
            Assert.Null(granted.Result);

            var denied = await Run(typeof(ModuleLevelController), nameof(ModuleLevelController.AnyAsync),
                new FakeChecker("user:create"));
            Assert.NotNull(denied.Result);
        }

        [Fact]
        public async Task 多个特性叠加_都要满足()
        {
            // 给了 create、没给 export → 仍然拒绝（AND 语义）
            var checker = new FakeChecker("user:create");
            var context = await Run(typeof(CreateAndExportController), nameof(CreateAndExportController.ExportAsync), checker);

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
            Assert.Equal(2, checker.Queried.Count);
        }

        [Fact]
        public async Task 未登录_按登录态无效拒绝()
        {
            var context = await Run(typeof(CreateController), nameof(CreateController.CreateAsync),
                new FakeChecker("user:create"), authenticated: false);

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.TokenEmpty, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 没注册判定服务_拒绝而不是放行()
        {
            // 正常应由启动期校验挡住；真走到这里必须 fail-closed
            var context = await Run(typeof(CreateController), nameof(CreateController.CreateAsync), checker: null);

            Assert.NotNull(context.Result);
            Assert.Equal((int)ApiResultCode.NoPermission, ((VivApiResult)context.Result!).Code);
        }

        [Fact]
        public async Task 匿名接口_优先放行()
        {
            var context = await Run(typeof(CreateController), nameof(CreateController.CreateAsync),
                new FakeChecker(), allowAnonymous: true);

            Assert.Null(context.Result);
        }

        [Fact]
        public void 空编码_构造即失败()
        {
            Assert.Throws<ArgumentException>(() => new VivRequirePermissionAttribute("  "));
        }
    }
}
