using Microsoft.AspNetCore.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Redis;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 注册Viv框架下的各种Context
    /// </summary>
    public class VivContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITokenService _tokenService;

        public VivContextMiddleware(RequestDelegate next, ITokenService tokenService)
        {
            _next = next;
            _tokenService = tokenService;
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
                else
                {
                    // 没有这三个数据 从token中获取
                    var token = context.GetJwtToken();
                    if (token.IsNullOrEmpty())
                    {
                        await context.SetApiResponse(ApiResultCode.TokenEmpty);
                        return;
                    }

                    if (!_tokenService.ValidateToken(token))
                    {
                        await context.SetApiResponse(ApiResultCode.TokenInvalid);
                        return;
                    }

                    var tokenInfo = _tokenService.ParseToken(token);
                    vivContext.SetAppId(tokenInfo.AppId);
                    vivContext.SetTenantId(tokenInfo.TenantId);
                    vivContext.SetUserId(tokenInfo.UserId);
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