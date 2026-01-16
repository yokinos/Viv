using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    /// <summary>
    /// Viv API 通用响应封装
    /// </summary>
    public class VivApiResult
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
    }
}
