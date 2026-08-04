using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine.Power;

namespace Viv.Engine
{
    /// <summary>
    /// 默认上下文提供者 —— 从 Header 或 JWT Token 中提取身份
    /// 如果子服务没有注册自己的实现，则使用这个
    /// </summary>
    public class DefaultVivContextProvider : IVivContextProvider
    {
        private readonly RequestTokenAnalysisMagic _requestMagic;

        public DefaultVivContextProvider(RequestTokenAnalysisMagic requestMagic)
        {
            _requestMagic = requestMagic;
        }

        public virtual async Task<VivContextModel?> GetContextAsync(HttpContext context, CancellationToken cancellationToken = default)
        {
            // 优先从 Header 提取
            var headerContext = _requestMagic.GetContextFromHeaders(context);
            if (headerContext != null)
                return headerContext;

            // 其次从 JWT Token 提取
            return await _requestMagic.GetContextFromTokenAsync(context);
        }

        public virtual bool ShouldSkip(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            if (endpoint == null)
                return true;

            var allowAnonymous = endpoint.Metadata?.GetMetadata<AllowAnonymousAttribute>() != null;
            if (allowAnonymous)
            {
                return allowAnonymous;
            }

            var httpMethodMetadata = endpoint.Metadata?.GetMetadata<IHttpMethodMetadata>();
            if (httpMethodMetadata != null)
            {
                var requestMethod = context.Request.Method;
                if (!httpMethodMetadata.HttpMethods.Contains(requestMethod, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var isApiRequest = context.Request.Path.HasValue && context.Request.Path.Value.StartsWith("/api");
            return !isApiRequest;
        }
    }
}
