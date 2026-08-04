using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Extension;

namespace Viv.Engine.Power
{
    public class RequestTokenAnalysisMagic : IDependency
    {
        public const string AppIdHeader = "x-viv-appId"; // 这个指的是客户端的AppId
        public const string SubjectIdHeader = "x-viv-subjectId";
        public const string UserIdHeader = "x-viv-userId";
        public const string ServiceNameHeader = "x-viv-serviceName"; // 这个指的是服务的名称，比如 viv.apex.api
        public const string InnerRequestTokenHeader = "x-request-token"; // 这个指的是内部请求的 Token，于验证内部请求的合法性，设置到appsettings.json 中设置。

        private readonly ITokenService _tokenService;

        public RequestTokenAnalysisMagic(ITokenService tokenService)
        {
            _tokenService = tokenService;
        }

        /// <summary>
        /// 从可信内部请求 Header 中获取上下文
        /// </summary>
        public VivContextModel? GetContextFromHeaders(HttpContext context)
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

            return new VivContextModel
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId
            };
        }

        /// <summary>
        /// 从 JWT Token 中获取上下文。
        /// </summary>
        public async Task<VivContextModel?> GetContextFromTokenAsync(HttpContext context)
        {
            var token = context.GetJwtToken();

            if (token.IsNullOrEmpty())
            {
                return null;
            }

            bool tokenIsValid = _tokenService.ValidateToken(token);
            if (!tokenIsValid)
            {
                return null;
            }

            try
            {
                var tokenInfo = _tokenService.ParseToken(token);

                if (tokenInfo == null || tokenInfo.AppId <= 0 || tokenInfo.UserId <= 0)
                {
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
