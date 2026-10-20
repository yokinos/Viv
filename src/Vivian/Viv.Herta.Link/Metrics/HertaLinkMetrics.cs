using System.Diagnostics.Metrics;

namespace Viv.Herta.Link.Metrics
{
    public static class HertaLinkMetrics
    {
        public const string MeterName = "Viv.Herta.Link";

        private static readonly Meter Meter = new(MeterName, "1.0.0");

        /// <summary>本实例活跃连接数</summary>
        private static readonly UpDownCounter<long> ConnectionCounter = Meter.CreateUpDownCounter<long>("herta.link.connections");

        /// <summary>推送次数，标签 kind=user|group|recall，result=ok|skipped|failed</summary>
        private static readonly Counter<long> DispatchCounter = Meter.CreateCounter<long>("herta.link.dispatch");

        /// <summary>发送者身份与信封不一致的条数，正常应恒为 0</summary>
        private static readonly Counter<long> IdentityMismatchCounter = Meter.CreateCounter<long>("herta.link.identity.mismatch");

        public static void ConnectionAdded() => ConnectionCounter.Add(1);

        public static void ConnectionRemoved() => ConnectionCounter.Add(-1);

        public static void RecordDispatch(string kind, string result) =>
            DispatchCounter.Add(1, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("result", result));

        public static void RecordIdentityMismatch() => IdentityMismatchCounter.Add(1);
    }
}
