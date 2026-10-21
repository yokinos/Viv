using System.Diagnostics.Metrics;

namespace Viv.Kinna.Metrics
{
    /// <summary>
    /// 长连接层的指标。Meter 名要加进 Viv.Aspire.ServiceDefaults 的 AddMeter 名单才会进 OTLP。
    /// 指标都带 hub 标签，一个进程装多个 Hub 时能分开看。
    /// </summary>
    public static class KinnaMetrics
    {
        public const string MeterName = "Viv.Kinna";

        private static readonly Meter Meter = new(MeterName, "1.0.0");

        /// <summary>本副本活跃连接数</summary>
        private static readonly UpDownCounter<long> ConnectionCounter = Meter.CreateUpDownCounter<long>("kinna.connections");

        /// <summary>推送次数，标签 kind 由业务自定，result 是 ok/skipped/failed</summary>
        private static readonly Counter<long> DispatchCounter = Meter.CreateCounter<long>("kinna.dispatch");

        /// <summary>事件署名与调用者不一致的次数。模拟发送、机器人代发会出现在这里，不是错误</summary>
        private static readonly Counter<long> IdentityOverrideCounter = Meter.CreateCounter<long>("kinna.identity.override");

        public static void ConnectionAdded(string hub) => ConnectionCounter.Add(1, HubTag(hub));

        public static void ConnectionRemoved(string hub) => ConnectionCounter.Add(-1, HubTag(hub));

        public static void RecordDispatch(string hub, string kind, string result) =>
            DispatchCounter.Add(1,
                HubTag(hub),
                new KeyValuePair<string, object?>("kind", kind),
                new KeyValuePair<string, object?>("result", result));

        public static void RecordIdentityOverride(string hub) => IdentityOverrideCounter.Add(1, HubTag(hub));

        private static KeyValuePair<string, object?> HubTag(string hub) => new("hub", hub);
    }
}
