namespace Viv.Engine.Sse
{
    /// <summary>
    /// SSE 事件名（对应线上的 <c>event:</c> 字段）。
    /// 客户端用 <c>addEventListener</c> 按名订阅，不需要再解析 JSON 猜这一帧是什么。
    /// </summary>
    public static class VivSseEvents
    {
        /// <summary>
        /// 增量帧：业务数据分片，<c>data</c> 形如 <c>{"delta":"...","seq":1}</c>
        /// </summary>
        public const string Delta = "delta";

        /// <summary>
        /// 结束帧：正常收尾，<c>data</c> 是汇总（可为空）
        /// </summary>
        public const string Done = "done";

        /// <summary>
        /// 错误帧：服务端处理失败，<c>data</c> 形如 <c>{"code":-1,"message":"...","traceId":"..."}</c>
        /// </summary>
        public const string Error = "error";

        /// <summary>
        /// 取消帧：服务端主动中止（非客户端断开），<c>data</c> 带原因
        /// </summary>
        public const string Canceled = "canceled";
    }
}
