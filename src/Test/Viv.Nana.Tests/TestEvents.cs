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
}
