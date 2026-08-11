using Viv.Log;
using Viv.Nana.Core;

namespace Viv.Nana.Tests
{
    /// <summary>测试用消息体（事件后缀）</summary>
    public class TestApexEvent : NanaEvent
    {
        public string Payload { get; set; } = string.Empty;
    }

    /// <summary>小写 event 后缀，验证命名约定大小写不敏感</summary>
    public class LowerEvent : NanaEvent { }

    /// <summary>非 NanaEvent 的普通类，验证命名约定不要求 Event 后缀</summary>
    public class PlainMessage { }

    /// <summary>记录 Error 调用的内存日志桩</summary>
    public class StubLogger : ILoggerContract
    {
        public List<string> Errors { get; } = new();
        public List<(string Message, Exception? Ex)> ErrorWithException { get; } = new();

        public void Info(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Fatal(string message, params object[] args) { }
        public void Fatal(string message, Exception ex, params object[] args) { }
        public void Error(string message, params object[] args) => Errors.Add(message);
        public void Error(string message, Exception ex, params object[] args) => ErrorWithException.Add((message, ex));
    }
}
