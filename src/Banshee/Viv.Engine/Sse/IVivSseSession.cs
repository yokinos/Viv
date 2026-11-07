namespace Viv.Engine.Sse
{
    /// <summary>
    /// 一次 SSE 会话。业务代码只往它里面推事件，什么时候 flush、什么时候发心跳、
    /// 断了怎么收场，都由框架负责。
    /// </summary>
    /// <remarks>
    /// 生命周期是单向的：Open → (Done | Error | Canceled)，进入终态后再推事件返回 false，不会抛异常。
    /// </remarks>
    public interface IVivSseSession
    {
        /// <summary>
        /// 链路追踪标识，会自动写进每一帧的 data
        /// </summary>
        string? TraceId { get; }

        /// <summary>
        /// 最近一个已分配的事件序号
        /// </summary>
        long LastEventId { get; }

        /// <summary>
        /// 是否还能推事件（未进终态且链路未断）
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// 本次会话的取消令牌：客户端断开或响应中止时触发，业务循环应当尊重它
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// 推一帧任意事件。<paramref name="eventName"/> 用 <see cref="VivSseEvents"/> 或自定义名（如 tool_call）。
        /// 返回 false 表示会话已关闭，调用方应停止生产。
        /// </summary>
        ValueTask<bool> SendAsync(object? payload, string eventName = VivSseEvents.Delta);

        /// <summary>
        /// 推一段增量文本，data 为 <c>{"delta":"...","seq":n}</c>（seq 与线上 id 一致，便于客户端对账）
        /// </summary>
        ValueTask<bool> SendDeltaAsync(string delta);

        /// <summary>
        /// 正常收尾，写 done 帧并结束流。重复调用返回 false。
        /// </summary>
        Task<bool> CompleteAsync(object? summary = null);

        /// <summary>
        /// 失败收尾，写 error 帧并结束流。重复调用返回 false。
        /// </summary>
        Task<bool> FailAsync(string message, int code = -1);

        /// <summary>
        /// 服务端主动取消，写 canceled 帧并结束流。重复调用返回 false。
        /// </summary>
        Task<bool> CancelAsync(string? reason = null);
    }
}
