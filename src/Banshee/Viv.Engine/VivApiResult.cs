using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    /// <summary>
    /// Viv API 通用响应封装
    /// </summary>
    [Serializable]
    public class VivApiResult : IActionResult
    {
        public VivApiResult() { }
        public VivApiResult(int code, string message) : this(code, message, default) { }
        public VivApiResult(int code, string message, object? data)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        /// <summary>
        /// 状态码
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// 状态消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 数据
        /// </summary>
        public object? Data { get; set; }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;

            if (string.IsNullOrEmpty(response.ContentType) || !response.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                response.ContentType = "application/json; charset=UTF-8";
            }

            var jsonString = JsonConvert.SerializeObject(this, Formatting.None);
            await response.WriteAsync(jsonString);
        }
    }
}
