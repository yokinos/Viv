using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace Viv.Nana.Mqtt
{
    /// <summary>
    /// 收到的一条 MQTT 报文。框架只负责承载与反序列化，不解释 topic 与载荷的业务含义。
    /// </summary>
    public sealed class MqttMessage
    {
        public required string Topic { get; init; }

        public required byte[] Payload { get; init; }

        public MqttQualityOfServiceLevel Qos { get; init; }

        /// <summary>是否为 retained 消息（订阅命中保留消息时为 true）</summary>
        public bool Retain { get; init; }

        public IReadOnlyDictionary<string, string> UserProperties { get; init; } = new Dictionary<string, string>();

        public DateTimeOffset ReceivedAt { get; init; }

        public string AsText() => Payload.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Payload);

        public T? Deserialize<T>(JsonSerializerOptions? jsonOptions = null)
            => Payload.Length == 0 ? default : JsonSerializer.Deserialize<T>(Payload, jsonOptions);
    }
}
