using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Viv.Elysia.Sse
{
    /// <summary>
    /// SSE 帧信封：与普通接口的统一信封同形（code / message / data / traceId），
    /// 前端用同一套逻辑解析即可，不用为流式接口另写一套
    /// </summary>
    public class VivSseFrame
    {
        /// <summary>
        /// 业务码，0 表示正常
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 提示信息
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 数据体：增量帧为 { delta, seq }，结束帧为汇总
        /// </summary>
        [JsonPropertyName("data")]
        public object? Data { get; set; }

        /// <summary>
        /// 链路追踪标识
        /// </summary>
        [JsonPropertyName("traceId")]
        public string? TraceId { get; set; }

        /// <summary>
        /// 构造一个正常帧
        /// </summary>
        /// <param name="data">数据体</param>
        /// <param name="traceId">链路追踪标识</param>
        public static VivSseFrame Ok(object? data, string? traceId = null)
            => new() { Code = 0, Message = "ok", Data = data, TraceId = traceId };

        /// <summary>
        /// 构造一个结束帧
        /// </summary>
        /// <param name="summary">汇总数据（可为空）</param>
        /// <param name="traceId">链路追踪标识</param>
        public static VivSseFrame Done(object? summary = null, string? traceId = null)
            => new() { Code = 0, Message = "done", Data = summary, TraceId = traceId };

        /// <summary>
        /// 构造一个错误帧
        /// </summary>
        /// <param name="message">错误描述</param>
        /// <param name="traceId">链路追踪标识</param>
        public static VivSseFrame Fail(string message, string? traceId = null)
            => new() { Code = -1, Message = message, Data = null, TraceId = traceId };
    }
}
