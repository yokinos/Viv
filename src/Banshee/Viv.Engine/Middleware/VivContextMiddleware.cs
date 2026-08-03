using Microsoft.AspNetCore.Http;
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Extension;
using Viv.Redis;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] Viv 上下文中间件
    /// </summary>
    public class VivContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITokenService _tokenService;
        private const string AppIdHeader = "Viv_AppId";
        private const string SubjectIdHeader = "Viv_SubjectId";
        private const string UserIdHeader = "Viv_UserId";

        public VivContextMiddleware(RequestDelegate next, ITokenService tokenService)
        {
            _next = next;
            _tokenService = tokenService;
        }

        public async Task InvokeAsync(HttpContext context, IVivContext vivContext)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(vivContext);

            try
            {
                LockHolderContext.ResetHolderId();

                var headerContext = GetContextFromHeaders(context);
                if (headerContext != null)
                {
                    vivContext.SetSnapshot(headerContext);
                }
                else
                {
                    // 交由网关处理
                    //var tokenContext = await GetContextFromTokenAsync(context);
                    //if (tokenContext == null)
                    //{
                    //    return;
                    //}
                    //vivContext.SetSnapshot(tokenContext);
                }

                await _next(context).ConfigureAwait(false);
            }
            finally
            {
                LockHolderContext.Clear();
                vivContext.Clear();
            }
        }

        /// <summary>
        /// 从可信内部请求 Header 中获取上下文。
        /// 只有三个 Header 都有效时才认为是内部调用。
        /// </summary>
        private static VivContextModel? GetContextFromHeaders(HttpContext context)
        {
            if (!TryGetPositiveLong(context, AppIdHeader, out var appId))
            {
                return null;
            }

            if (!TryGetPositiveLong(context, UserIdHeader, out var userId))
            {
                return null;
            }

            TryGetPositiveLong(context, SubjectIdHeader, out var subjectId);

            return new Contracts.Models.VivContextModel
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId
            };
        }

        /// <summary>
        /// 从 JWT Token 中获取上下文。
        /// </summary>
        private async Task<VivContextModel?> GetContextFromTokenAsync(HttpContext context)
        {
            var token = context.GetJwtToken();

            if (token.IsNullOrEmpty())
            {
                await context.SetApiResponse(ApiResultCode.TokenEmpty).ConfigureAwait(false);
                return null;
            }

            bool tokenIsValid = _tokenService.ValidateToken(token);
            if (!tokenIsValid)
            {
                await context.SetApiResponse(ApiResultCode.TokenInvalid).ConfigureAwait(false);
                return null;
            }

            try
            {
                var tokenInfo = _tokenService.ParseToken(token);

                if (tokenInfo == null || tokenInfo.AppId <= 0 || tokenInfo.TenantId <= 0 || tokenInfo.UserId <= 0)
                {
                    await context.SetApiResponse(ApiResultCode.TokenInvalid).ConfigureAwait(false);
                    return null;
                }

                return new VivContextModel
                {
                    AppId = tokenInfo.AppId,
                    SubjectId = tokenInfo.TenantId,
                    UserId = tokenInfo.UserId
                };
            }
            catch
            {
                await context.SetApiResponse(ApiResultCode.TokenInvalid).ConfigureAwait(false);
                return null;
            }
        }

        /// <summary>
        /// 从请求 Header 中读取大于 0 的 long 值。
        /// </summary>
        private static bool TryGetPositiveLong(HttpContext context, string headerName, out long value)
        {
            value = 0;

            if (!context.Request.Headers.TryGetValue(headerName, out var headerValue))
            {
                return false;
            }

            var text = headerValue.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                value = 0;
                return false;
            }

            if (value <= 0)
            {
                value = 0;
                return false;
            }

            return true;
        }
    }
}