using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Viv.Momo.Core
{
    /// <summary>
    /// EF 命令耗时的采集点。挂在 EFAppContext 上，覆盖全部 EF 查询与 SaveChanges 的耗时与慢查询。
    ///
    /// 只覆写 Executed 那一半：耗时来自 <see cref="CommandExecutedEventData.Duration"/>（EF 自己在命令执行
    /// 上量的那段），不用自己起 Stopwatch，也就没有「起表与停表之间怎么传」这个问题。
    ///
    /// 每个 EFAppContext 实例一份，不是单例 —— 它带着那一份 QueryTelemetry（含 logger 与阈值）。
    /// </summary>
    internal sealed class MomoMetricsCommandInterceptor : DbCommandInterceptor
    {
        private const string Path = "ef";

        private readonly QueryTelemetry _telemetry;

        public MomoMetricsCommandInterceptor(QueryTelemetry telemetry)
        {
            _telemetry = telemetry;
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            _telemetry.Record(Path, "reader", (long)eventData.Duration.TotalMilliseconds, command.CommandText);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            _telemetry.Record(Path, "reader", (long)eventData.Duration.TotalMilliseconds, command.CommandText);
            return new ValueTask<DbDataReader>(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            _telemetry.Record(Path, "nonquery", (long)eventData.Duration.TotalMilliseconds, command.CommandText);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            _telemetry.Record(Path, "nonquery", (long)eventData.Duration.TotalMilliseconds, command.CommandText);
            return new ValueTask<int>(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            _telemetry.Record(Path, "scalar", (long)eventData.Duration.TotalMilliseconds, command.CommandText);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            _telemetry.Record(Path, "scalar", (long)eventData.Duration.TotalMilliseconds, command.CommandText);
            return new ValueTask<object?>(result);
        }
    }
}
