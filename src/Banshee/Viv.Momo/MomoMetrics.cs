using System.Diagnostics;
using System.Diagnostics.Metrics;
using Viv.Contracts.Enums;

namespace Viv.Momo
{
    /// <summary>
    /// 数据库访问的运行指标。Meter 名 Viv.Momo，Aspire ServiceDefaults 会把它挂进 OTel。
    ///
    /// 与 NanaMetrics / OutboxMetrics 同形：纯静态、只认「记什么」，不管「谁来记」。
    /// 慢查询的阈值判断与日志落在 QueryTelemetry，不在这里。
    ///
    /// 耗时指标覆盖全部库访问。EF 走 DbCommandInterceptor 逐条上报，Dapper 的 34 条命令都套了
    /// QueryTelemetry 的计时助手。样本数按真实 SQL 条数算，一次 PageAsync 两条，
    /// 一次批量 Insert 是按 200 分页后的条数。
    /// </summary>
    public static class MomoMetrics
    {
        public const string MeterName = "Viv.Momo";

        public const string QueryDurationInstrument = "viv.momo.query.duration";
        public const string SlowQueryInstrument = "viv.momo.query.slow";
        public const string ErrorsInstrument = "viv.momo.errors";
        public const string BatchPathInstrument = "viv.momo.batch.path";

        internal static readonly Meter Meter = new(MeterName, "1.0.0");

        internal static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(QueryDurationInstrument, "ms");
        internal static readonly Counter<long> SlowQuery = Meter.CreateCounter<long>(SlowQueryInstrument);
        internal static readonly Counter<long> Errors = Meter.CreateCounter<long>(ErrorsInstrument);
        internal static readonly Counter<long> BatchPath = Meter.CreateCounter<long>(BatchPathInstrument);

        /// <summary>
        /// 一次 SQL 命令的耗时。path 取 ef 或 dapper，op 取 reader / nonquery / scalar / page，
        /// dapper 侧另有 insert / update / delete 三个批量写取值。慢查询日志与这条指标共用
        /// QueryTelemetry 的判据，不要绕开它直接记。
        /// </summary>
        internal static void RecordQuery(string path, string op, long elapsedMs)
        {
            var tags = new TagList { { "path", path }, { "op", op } };
            QueryDuration.Record(elapsedMs, tags);
        }

        /// <summary>
        /// 超过 SlowQueryThresholdMs 的查询。与 query.duration 记的是同一次调用，这边只多一个计数。
        /// </summary>
        internal static void RecordSlowQuery(string path, string op)
        {
            var tags = new TagList { { "path", path }, { "op", op } };
            SlowQuery.Add(1, tags);
        }

        /// <summary>
        /// 数据库访问失败。收口在 WrapDatabaseException，所以 45 个调用点一处都不用改。
        /// </summary>
        internal static void RecordError(VivConnType connType)
        {
            var tags = new TagList { { "type", connType.ToString().ToLowerInvariant() } };
            Errors.Add(1, tags);
        }

        /// <summary>
        /// 批量操作走了哪条路。EFMaxCount 把批量写分成 EF 与 Dapper 两条，这里让分流看得见。
        /// op 取 insert / update / delete。
        /// </summary>
        internal static void RecordBatchPath(string path, string op)
        {
            var tags = new TagList { { "path", path }, { "op", op } };
            BatchPath.Add(1, tags);
        }
    }
}
