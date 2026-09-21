using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Viv.Outbox
{
    /// <summary>
    /// 发件箱运行指标。Meter 名 <c>Viv.Outbox</c>：入队计数、投递时延、待投深度、失败条数。
    /// </summary>
    public static class OutboxMetrics
    {
        public const string MeterName = "Viv.Outbox";

        public const string EnqueuedInstrument = "viv.outbox.enqueued";
        public const string DeliveredInstrument = "viv.outbox.delivered";
        public const string FailedInstrument = "viv.outbox.failed";
        public const string PublishDurationInstrument = "viv.outbox.publish.duration";
        public const string PendingGauge = "viv.outbox.pending";
        public const string FailedGauge = "viv.outbox.failed_depth";

        internal static readonly Meter Meter = new(MeterName, "1.0.0");

        internal static readonly Counter<long> Enqueued = Meter.CreateCounter<long>(EnqueuedInstrument);
        internal static readonly Counter<long> Delivered = Meter.CreateCounter<long>(DeliveredInstrument);
        internal static readonly Counter<long> Failed = Meter.CreateCounter<long>(FailedInstrument);
        internal static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>(PublishDurationInstrument, "ms");

        private static long _pendingDepth;
        private static long _failedDepth;

        static OutboxMetrics()
        {
            Meter.CreateObservableGauge(PendingGauge, () => Volatile.Read(ref _pendingDepth));
            Meter.CreateObservableGauge(FailedGauge, () => Volatile.Read(ref _failedDepth));
        }

        internal static void RecordEnqueue() => Enqueued.Add(1);

        internal static void RecordDelivered(long elapsedMs)
        {
            Delivered.Add(1);
            PublishDuration.Record(elapsedMs);
        }

        internal static void RecordFailed() => Failed.Add(1);

        internal static void SetDepth(long pending, long failed)
        {
            Volatile.Write(ref _pendingDepth, pending);
            Volatile.Write(ref _failedDepth, failed);
        }
    }
}
