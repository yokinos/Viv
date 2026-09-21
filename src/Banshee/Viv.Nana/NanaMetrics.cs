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
    }
}
