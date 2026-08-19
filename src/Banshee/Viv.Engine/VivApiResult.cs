using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Sandrone.Conveter;

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
        /// 请求Id
        /// </summary>
        public string RequestId { get; set; }

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

            // 设置请求Id 此Id由网关生成
            RequestId = context.HttpContext.TraceIdentifier;

            // 业务在返回前先设置、且在 VivRunDefine 白名单内的状态码（301/302/304/404 等）原样保留；
            // 其余场景统一 200（业务信封语义）。
            response.StatusCode = VivRunDefine.AllowedHttpStatusCodes.Contains(response.StatusCode)
                ? response.StatusCode
                : (int)HttpStatusCode.OK;

            var jsonString = JsonConvert.SerializeObject(this, Formatting.None, JsonNetSetting.ApiResponseSettings);
            await response.WriteAsync(jsonString);
        }

        public static VivApiResult Success(string message, object? data = null)
        {
            return ApiRsult(ApiResultCode.Success, message, data);
        }

        public static VivApiResult Success(object? data = null)
        {
            return ApiRsult(ApiResultCode.Success, "successful", data);
        }

        public static VivApiResult Failed(string message, object? data = null)
        {
            return ApiRsult(ApiResultCode.Error, message, data);
        }

        public static VivApiResult ApiRsult(ApiResultCode code, string? message = null, object? data = null)
        {
            message ??= code.GetDescription();
            return new VivApiResult((int)code, message, data);
        }
    }
}
