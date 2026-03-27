using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Viv.Engine.Conveter;

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


        protected static readonly JsonSerializerSettings _jsonSettings = new()
        {
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            ContractResolver = new VivContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
        };

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;

            if (string.IsNullOrEmpty(response.ContentType) || !response.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                response.ContentType = "application/json; charset=UTF-8";
            }

            response.StatusCode = (int)HttpStatusCode.OK;

            var jsonString = JsonConvert.SerializeObject(this, Formatting.None, _jsonSettings);
            await response.WriteAsync(jsonString);
        }

        public static VivApiResult Success(string message = "successful", object? data = null)
        {
            return ApiRsult(ResultCode.Success, message, data);
        }

        public static VivApiResult Error(string message, object? data = null)
        {
            return ApiRsult(ResultCode.Error, message, data);
        }

        public static VivApiResult ApiRsult(int code, string message, object? data = null)
        {
            return new VivApiResult(code, message, data);
        }
    }
}
