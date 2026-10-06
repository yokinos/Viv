using MQTTnet.Protocol;

namespace Viv.Nana.Mqtt
{
    /// <summary>
    /// MQTT 客户端：只提供连接、发布、订阅这套机制。
    /// topic 约定、机器标识、载荷类型、幂等键全部属于业务 —— 框架对「设备」一无所知。
    /// </summary>
    public interface IMqttClientService
    {
        bool IsConnected { get; }

        /// <summary>断线期间排队待发的报文数</summary>
        int PendingMessages { get; }

        /// <summary>按 JSON 发布（载荷序列化成 UTF-8 JSON）</summary>
        Task PublishJsonAsync<T>(string topic, T payload, MqttQualityOfServiceLevel? qos = null, bool? retain = null, CancellationToken cancellationToken = default);

        /// <summary>按原始字节发布</summary>
        Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQualityOfServiceLevel? qos = null, bool? retain = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 订阅。topicFilter 支持 + / # 通配符。
        /// sharedGroup 为空 = 每个实例各收一份（fanout）；给了值 = $share/{group}/ 共享订阅，多实例间只处理一次。
        /// 返回对象释放即退订。
        /// </summary>
        Task<IDisposable> SubscribeAsync(string topicFilter, string? sharedGroup, Func<MqttMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default);
    }
}
