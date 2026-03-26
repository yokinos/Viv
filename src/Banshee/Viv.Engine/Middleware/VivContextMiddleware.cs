using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using Viv.Authentication;
using Viv.Contracts.Interface;
using Viv.Redis;
using Viv.Vva.Extension;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 注册Viv框架下的各种Context（微服务网关版）
    /// </summary>
    public class VivContextMiddleware
    {
        private readonly RequestDelegate _next;

        public VivContextMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IVivContext vivContext)
        {
            try
            {
                LockHolderContext.ResetHolderId();

                var appId = context.Request.Headers["Viv_AppId"].ToString().As<long>();
                var tenantId = context.Request.Headers["Viv_TenantId"].ToString().As<long>();
                var userId = context.Request.Headers["Viv_UserId"].ToString().As<long>();

                if (appId > 0 && userId > 0)
                {
                    vivContext.SetAppId(appId);
                    vivContext.SetTenantId(tenantId);
                    vivContext.SetUserId(userId);
                }

                await _next(context).ConfigureAwait(false);
            }
            finally
            {
                LockHolderContext.Clear();
                vivContext.Clear();
            }
        }
    }
}