using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Viv.Delusion.Extension;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Engine.HealthChecks
{
    /// <summary>
    /// 数据库连通性检查，跑一条 SELECT 1。
    ///
    /// 只注入 IOptions&lt;DatabaseOptions&gt;，绝不注入 IMomoDbContext：AddCheck 用
    /// ActivatorUtilities.GetServiceOrCreateInstance 在根 provider 上建实例，从根解析 Scoped 的
    /// IMomoDbContext 会拿到一个跨请求共享的实例 —— 正是 LocalEventBus 当初绕开 IServiceProvider
    /// 的那个坑。顺带也躲开了 EF 模型初始化（本身是慢操作），健康探测里不该带上它。
    ///
    /// 打 ready 不打 live：库连不上确实该摘流量，但不该被当成进程要重启。
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly DatabaseOptions _options;

        public DatabaseHealthCheck(IOptions<DatabaseOptions> options)
        {
            _options = options.Value;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // 无论是否读写分离，主库连接都是必填的（从库可以没有），拿它当判据
            var connectionString = _options.MasterConnectionString;
            if (connectionString.IsNullOrEmpty())
            {
                return HealthCheckResult.Unhealthy("未配置主库连接字符串");
            }

            try
            {
                // 建连接也在 try 里：库类型配错同样是「探不动」，一并归到 Unhealthy
                await using var connection = CreateConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.CommandTimeout = _options.Timeout;
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("数据库不可达", ex);
            }
        }

        /// <summary>
        /// 按配置的库类型建连接。与走 EF（UseNpgsql / UseSqlServer）那边保持一致：
        /// SQL Server 用 Microsoft.Data.SqlClient，与 EF Core 的 SqlServer 提供程序是同一个客户端
        /// </summary>
        private DbConnection CreateConnection(string connectionString) => _options.DatabaseSource switch
        {
            DatabaseSourceType.PostgreSQL => new NpgsqlConnection(connectionString),
            DatabaseSourceType.SqlServer => new SqlConnection(connectionString),
            _ => throw new NotSupportedException($"健康检查不支持的数据库类型：{_options.DatabaseSource}")
        };
    }
}
