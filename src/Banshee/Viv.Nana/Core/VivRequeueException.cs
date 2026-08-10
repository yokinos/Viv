namespace Viv.Nana.Core
{
    /// <summary>
    /// 消费者要求重新投递消息（重试）的异常信号。
    /// 被全局失败策略（VivWolverineConfigurationExtensions 中 RetryWithCooldown）捕获：
    /// 按 NanaOptions.RetryCount 重试 → 耗尽后进死信队列。
    /// </summary>
    public class VivRequeueException : Exception
    {
        public VivRequeueException(string message) : base(message) { }

        public VivRequeueException(string message, Exception inner) : base(message, inner) { }
    }
}
