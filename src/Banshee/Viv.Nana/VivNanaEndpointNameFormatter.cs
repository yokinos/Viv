using MassTransit;

namespace Viv.Nana
{
    /// <summary>
    /// Queue 命名规则：{EventName}Queue（去 Event 后缀）
    /// TestApexEvent → TestApexQueue
    /// </summary>
    public class VivNanaEndpointNameFormatter : DefaultEndpointNameFormatter
    {
        public new static readonly VivNanaEndpointNameFormatter Instance = new();

        public VivNanaEndpointNameFormatter() : base(false) { }

        public override string Consumer<T>()
        {
            var msgType = NanaRegister.ExtractMessageType(typeof(T));
            return msgType != null
                ? NanaRegister.GetQueueName(msgType)
                : base.Consumer<T>();
        }
    }
}
