using System.Diagnostics;
using Viv.Delusion.Extension;
using Viv.Log;

namespace Viv.Momo
{
    /// <summary>
    /// 查询耗时与慢查询的唯一判据。EF 拦截器与全部 Dapper 执行点都走这里，口径只有这一份。
    ///
    /// 单独成一个类而不是塞进 MomoMetrics：指标是「记什么」，阈值与日志是「谁来记」。
    /// 混在一起之后 MomoMetrics 就不再是那个可以照着 NanaMetrics / OutboxMetrics 无脑抄的模板了。
    ///
    /// 公开是因为 EFAppContext 的公开构造函数要收它。实例由 MomoDatabase 建（_logger 与
    /// SlowQueryThresholdMs 都是现成字段），随 EFAppContext 一起下发。
    /// </summary>
    public sealed class QueryTelemetry
    {
        /// <summary>
        /// 慢查询日志里 SQL 的截断长度。日志是给人扫的，不是给回放用的 —— 整条 SQL 会淹掉上下文
        /// </summary>
        private const int MaxSqlLength = 200;

        private readonly ILoggerContract _logger;
        private readonly int _slowQueryThresholdMs;

        public QueryTelemetry(ILoggerContract logger, int slowQueryThresholdMs)
        {
            _logger = logger;
            _slowQueryThresholdMs = slowQueryThresholdMs;
        }

        /// <summary>
        /// 记一次 SQL 命令：先记耗时指标，再比阈值，超了加慢查询计数并记一条 Warning。
        /// </summary>
        /// <param name="path">ef 或 dapper</param>
        /// <param name="op">
        /// reader / nonquery / scalar / page，Dapper 侧另有 insert / update / delete。
        ///
        /// 两边的 scalar 不是一个意思：ef 的来自 ExecuteScalar（单个标量值），
        /// dapper 的指单行读（QueryFirstOrDefault 一族，底层其实是 ExecuteReader）。
        /// </param>
        /// <param name="elapsedMs">本次耗时</param>
        /// <param name="sql">命令文本，可为空（拿不到最终拼好的 SQL 时传手上那条原串）</param>
        public void Record(string path, string op, long elapsedMs, string? sql)
        {
            MomoMetrics.RecordQuery(path, op, elapsedMs);

            if (_slowQueryThresholdMs <= 0 || elapsedMs < _slowQueryThresholdMs)
                return;

            MomoMetrics.RecordSlowQuery(path, op);
            _logger.Warning($"慢查询 {elapsedMs}ms（阈值 {_slowQueryThresholdMs}ms）path:{path} op:{op} SQL:{Truncate(sql)}");
        }

        /// <summary>
        /// 包住一次 SQL 执行并记耗时。34 个 Dapper 执行点都用它，path 与 op 只写在这儿。
        ///
        /// 记录放在 finally，超时和断连也记得上。只在成功时记的话，最慢的那批反而看不到。
        /// </summary>
        public T Measure<T>(string path, string op, string? sql, Func<T> body)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                return body();
            }
            finally
            {
                Record(path, op, sw.ElapsedMilliseconds, sql);
            }
        }

        /// <inheritdoc cref="Measure{T}" />
        public async Task<T> MeasureAsync<T>(string path, string op, string? sql, Func<Task<T>> body)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                return await body();
            }
            finally
            {
                Record(path, op, sw.ElapsedMilliseconds, sql);
            }
        }

        /// <summary>
        /// 截断 SQL。空串单独给个占位符，免得日志里 `SQL:` 后面空着看不出是「没有」还是「没取到」
        /// </summary>
        private static string Truncate(string? sql)
        {
            if (sql.IsNullOrEmpty())
                return "<none>";

            return sql.Length <= MaxSqlLength ? sql : sql[..MaxSqlLength] + "...";
        }
    }
}
