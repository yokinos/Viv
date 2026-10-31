using System.Text.Json.Serialization;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// SSE 帧信封。字段名与普通接口信封一样（code / message / data / traceId），
    /// 成功码仍是 0，结束帧的 message 是 done，和 REST 的 200 不是同一套。
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
        public static VivSseFrame Ok(object? data, string? traceId = null)
            => new() { Code = 0, Message = "ok", Data = data, TraceId = traceId };

        /// <summary>
        /// 构造一个结束帧
        /// </summary>
        public static VivSseFrame Done(object? summary = null, string? traceId = null)
            => new() { Code = 0, Message = "done", Data = summary, TraceId = traceId };

        /// <summary>
        /// 构造一个错误帧
        /// </summary>
        public static VivSseFrame Fail(string message, string? traceId = null)
            => new() { Code = -1, Message = message, Data = null, TraceId = traceId };
    }
}
