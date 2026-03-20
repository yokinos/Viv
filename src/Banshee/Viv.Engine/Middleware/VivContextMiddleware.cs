using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using Viv.Authentication;
using Viv.Contracts.Interface;
using Viv.Log.VivLogger;
using Viv.Redis;
using Viv.Vva.Extension;

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
                // 重置[分布式锁]在当前请求的持有者信息
                LockHolderContext.ResetHolderId();

                // 获取 Token
                var token = context.GetJwtToken();
                if (string.IsNullOrEmpty(token))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                // 验证 Token 是否有效
                if (!_tokenService.ValidateToken(token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                // 解析 Token
                var tokenInfo = _tokenService.ParseToken(token);
                if (tokenInfo == null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                // 注入上下文
                vivContext.SetAppId(tokenInfo.AppId);
                vivContext.SetTenantId(tokenInfo.TenantId);
                vivContext.SetUserId(tokenInfo.UserId);

                // 执行后续中间件
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            }
            finally
            {
                // 清理上下文，避免内存泄漏/污染
                LockHolderContext.Clear();
                vivContext.Clear();
            }
        }
    }
}