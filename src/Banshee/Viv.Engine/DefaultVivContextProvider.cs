using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
            // 默认跳过策略：检查是否标记了 AllowAnonymous
            var endpoint = context.GetEndpoint();
            return endpoint?.Metadata?.GetMetadata<AllowAnonymousAttribute>() != null;
        }
    }
}
