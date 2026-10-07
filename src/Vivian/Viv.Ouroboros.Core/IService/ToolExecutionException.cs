using System;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 工具"执行到了、但业务上失败"（典型是 HTTP 非 2xx）。
    ///
    /// 由注册表的执行外壳接住：留痕记失败，同时把 <see cref="Exception.Message"/> 当作工具结果交回模型，
    /// 让模型自己决定重试还是改参数。与"控制流异常直接抛出去"是两回事 —— 后者会打死一整轮对话。
    /// </summary>
    public class ToolExecutionException : Exception
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="message">给模型看的原因（会作为工具结果回传，可以很长）</param>
        /// <param name="reason">留痕用的短原因（如 "HTTP 500"）；不给就退回 <paramref name="message"/></param>
        /// <param name="latencyMs">执行器报的耗时（毫秒）；拿不到就不给，留痕退回外壳掐表</param>
        public ToolExecutionException(string message, string? reason = null, long? latencyMs = null) : base(message)
        {
            Reason = reason;
            LatencyMs = latencyMs;
        }

        /// <summary>
        /// 留痕用的短原因。模型看的是 <see cref="Exception.Message"/>（可能整段响应体），
        /// 留痕的失败原因该是能一眼扫过的那种。
        /// </summary>
        public string? Reason { get; }

        /// <summary>
        /// 执行器报的耗时（毫秒）。外层掐表会把自己的调度开销也算进去，所以能拿到就用这个。
        /// </summary>
        public long? LatencyMs { get; }
    }
}
