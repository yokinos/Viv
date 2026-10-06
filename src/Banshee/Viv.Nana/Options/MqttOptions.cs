using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Viv.Nana.Options
{
    /// <summary>
    /// MQTT 接入配置（EMQX）。为 null = 不启用，与 NanaOption / OutboxOption 同一语义。
    /// 只描述连接与协议参数，不含任何业务 topic 约定 —— topic 结构与机器标识属于业务。
    /// </summary>
    public class MqttOptions
    {
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 1883;

        public bool UseTls { get; set; } = false;

        /// <summary>空 = 自动生成 viv-{入口程序集名}-{随机后缀}</summary>
        public string ClientId { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        /// <summary>默认 3.1.1：设备侧协议版本参差，平台自己不需要 v5 特性；要用 v5 改这里</summary>
        public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V311;

        public bool CleanSession { get; set; } = true;

        public int KeepAliveSeconds { get; set; } = 60;

        public int ConnectTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 断线自动重连间隔
        /// </summary>
        public int ReconnectDelaySeconds { get; set; } = 5;

        /// <summary>
        /// 断线期间待发报文的缓冲上限
        /// </summary>
        public int MaxPendingMessages { get; set; } = 1000;

        public MqttQualityOfServiceLevel DefaultPublishQos { get; set; } = MqttQualityOfServiceLevel.AtLeastOnce;

        public bool DefaultPublishRetain { get; set; } = false;
    }
}
