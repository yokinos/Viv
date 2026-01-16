using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine.Options
{
    /// <summary>
    /// Viv API 请求结果选项
    /// </summary>
    public record VivApiResultOptions
    {
        /// <summary>
        /// 请求成功返回码
        /// </summary>
        public int SuccessCode { get; init; } = 200;

        /// <summary>
        /// 请求失败通用返回码
        /// </summary>
        public int ErrorCode { get; init; } = -200;

        /// <summary>
        /// 是否在API返回结果中携带RequestId
        /// 若关闭则默认返回空字符串
        /// 若开启则会在请求管道中自动生成RequestId并返回
        /// </summary>
        public bool IsReturnRequestId { get; init; } = false;
    }
}