using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Models;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 上下文提供者接口 —— 子服务可以自己决定怎么解析身份
    /// </summary>
    public interface IVivContextProvider
    {
        /// <summary>
        /// 从当前 HttpContext 中提取身份信息
        /// </summary>
        /// <param name="context">当前 HttpContext</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>提取到的上下文，如果返回 null 表示提取失败或无身份</returns>
        Task<VivContextModel?> GetContextAsync(HttpContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// 判断当前请求是否应该跳过身份提取（比如匿名端点、健康检查等）
        /// </summary>
        bool ShouldSkip(HttpContext context);
    }
}
