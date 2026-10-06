using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Viv.Nana
{
    /// <summary>
    /// Nana 发布/消费的运行指标。Meter 名 <c>Viv.Nana</c>，Aspire ServiceDefaults 会把它挂进 OTel。
    /// </summary>
    public static class NanaMetrics
    {
        public const string MeterName = "Viv.Nana";

        public const string PublishedInstrument = "viv.nana.published";
        public const string ConsumedInstrument = "viv.nana.consumed";
        public const string PublishDurationInstrument = "viv.nana.publish.duration";
        public const string ConsumeDurationInstrument = "viv.nana.consume.duration";

        internal static readonly Meter Meter = new(MeterName, "1.0.0");

        internal static readonly Counter<long> Published = Meter.CreateCounter<long>(PublishedInstrument);
        internal static readonly Counter<long> Consumed = Meter.CreateCounter<long>(ConsumedInstrument);
        internal static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>(PublishDurationInstrument, "ms");
        internal static readonly Histogram<double> ConsumeDuration = Meter.CreateHistogram<double>(ConsumeDurationInstrument, "ms");

        internal static void RecordPublish(string eventType, long elapsedMs)
        {
            var tags = new TagList { { "event.type", eventType } };
            Published.Add(1, tags);
            PublishDuration.Record(elapsedMs, tags);
        }

        internal static void RecordConsume(string eventType, long elapsedMs)
        {
            var tags = new TagList { { "event.type", eventType } };
            Consumed.Add(1, tags);
            ConsumeDuration.Record(elapsedMs, tags);
        }

        #region MQTT（设备接入）

        public const string MqttPublishedInstrument = "viv.nana.mqtt.published";
        public const string MqttReceivedInstrument = "viv.nana.mqtt.received";
        public const string MqttErrorInstrument = "viv.nana.mqtt.errors";

        internal static readonly Counter<long> MqttPublished = Meter.CreateCounter<long>(MqttPublishedInstrument);
        internal static readonly Counter<long> MqttReceived = Meter.CreateCounter<long>(MqttReceivedInstrument);
        internal static readonly Counter<long> MqttErrors = Meter.CreateCounter<long>(MqttErrorInstrument);

        // 标签只取 topic 的第一段：设备 topic 里带 machineId，整串进标签会炸成几千条时间序列
        private static string MqttRoot(string topic)
        {
            var i = topic.IndexOf('/');
            return i > 0 ? topic[..i] : topic;
        }

        internal static void RecordMqttPublish(string topic)
            => MqttPublished.Add(1, new TagList { { "mqtt.root", MqttRoot(topic) } });

        internal static void RecordMqttReceive(string topic)
            => MqttReceived.Add(1, new TagList { { "mqtt.root", MqttRoot(topic) } });

        internal static void RecordMqttError(string reason)
            => MqttErrors.Add(1, new TagList { { "reason", reason } });

        #endregion
    }
}
