using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Redis;

namespace Viv.Engine
{
    /// <summary>
    /// 通过扩展方法注入整个Web框架的Viv相关封装功能
    /// </summary>
    public static class VivEngineExtensions
    {
        /// <summary>
        /// 判断是否为Ajax请求
        /// </summary>
        public static bool IsAjax(this HttpRequest request, string rule = "")
        {
            // 判断是否为Post请求
            bool isPost = request.Method.Equals("Post", StringComparison.OrdinalIgnoreCase);

            // Ajax请求判断
            bool isAjax = request.Headers["X-Requested-With"] == "XMLHttpRequest";

            // 接口路径判断
            bool isApiPath = !string.IsNullOrEmpty(rule) && request.Path.Value.Contains(rule, StringComparison.OrdinalIgnoreCase);

            return isPost || isAjax || isApiPath;
        }

        /// <summary>
        /// 获取请求头中的Token信息
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static string GetJwtToken(this HttpContext context)
        {
            return context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        }
    }
}
