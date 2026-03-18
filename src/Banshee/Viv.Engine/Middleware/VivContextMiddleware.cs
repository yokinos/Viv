using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Engine.Middleware
{
    /// <summary>
    /// [中间件] 注册Viv框架下的各种Context
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
                LockHolderContext.Clear();
                var x = LockHolderContext.CurrentHolderId; //调用一次 直接生成
                // 解析登录对象

                vivContext.SetAppId(0);
                vivContext.SetTenantId(0);
                vivContext.SetUserId(0);

                await _next(context);
            }
            finally
            {
                LockHolderContext.Clear();
                vivContext.Clear();
            }
        }
    }
}
