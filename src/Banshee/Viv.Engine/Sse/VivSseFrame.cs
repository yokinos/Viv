using System.Text.Json.Serialization;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// SSE 帧。<see cref="Event"/> 与 <see cref="Id"/> 是线上的 <c>event:</c> / <c>id:</c> 字段，
    /// 不进 <c>data</c>；<see cref="Code"/> / <see cref="Message"/> / <see cref="Data"/> / <see cref="TraceId"/>
    /// 序列化成 <c>data</c> 里的 JSON 信封，与普通接口信封保持一致的形状。
    /// </summary>
    public sealed class VivSseFrame
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
        /// 事件名，取 <see cref="VivSseEvents"/> 里的常量；只在线上出现，不写入 data
        /// </summary>
        [JsonIgnore]
        public string Event { get; set; } = VivSseEvents.Delta;

        /// <summary>
        /// 事件序号（单调递增，从 1 开始）。客户端断线重连时靠它续传；只在线上出现，不写入 data
        /// </summary>
        [JsonIgnore]
        public long Id { get; set; }

        /// <summary>
        /// 构造一个正常帧
        /// </summary>
        public static VivSseFrame Ok(object? data, string? traceId = null)
            => new() { Code = 0, Message = "ok", Data = data, TraceId = traceId };

        /// <summary>
        /// 构造一个结束帧
        /// </summary>
        public static VivSseFrame Done(object? summary = null, string? traceId = null)
            => new() { Code = 0, Message = "done", Data = summary, TraceId = traceId, Event = VivSseEvents.Done };

        /// <summary>
        /// 构造一个错误帧
        /// </summary>
        public static VivSseFrame Fail(string message, string? traceId = null, int code = -1)
            => new() { Code = code, Message = message, Data = null, TraceId = traceId, Event = VivSseEvents.Error };

        /// <summary>
        /// 构造一个取消帧
        /// </summary>
        public static VivSseFrame Canceled(string? reason = null, string? traceId = null)
            => new() { Code = -2, Message = reason ?? "canceled", Data = null, TraceId = traceId, Event = VivSseEvents.Canceled };
    }
}
