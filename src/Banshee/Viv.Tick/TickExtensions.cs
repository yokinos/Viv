using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using Viv.Delusion.Extension;
using Viv.Momo.Enums;
using Viv.Tick.Options;

namespace Viv.Tick
{
    public static class TickExtensions
    {
        public static IServiceCollection AddVivTickerQ(this IServiceCollection services, TickOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.TickerQ);
            var tickerOpt = options.TickerQ;

            services.AddTickerQ(opt =>
            {
                if (!string.IsNullOrEmpty(tickerOpt.ConnectionString))
                {
                    opt.AddOperationalStore(efOpt =>
                    {
                        efOpt.UseTickerQDbContext<TickerQDbContext>(dbOpt =>
                        {
                            switch (tickerOpt.DatabaseType)
                            {
                                case DatabaseSouceType.PostgreSQL:
                                    dbOpt.UseNpgsql(tickerOpt.ConnectionString);
                                    break;
                                case DatabaseSouceType.SqlServer:
                                    dbOpt.UseSqlServer(tickerOpt.ConnectionString);
                                    break;
                                default:
                                    throw new NotSupportedException($"TickerQ 暂不支持数据库类型: {tickerOpt.DatabaseType}");
                            }
                        });
                    });
                }

                if (tickerOpt.EnableDashboard)
                {
                    opt.AddDashboard(dashboard =>
                    {
                        dashboard.SetBasePath(tickerOpt.DashboardOptions.DashboardPath);
                        if (!string.IsNullOrEmpty(tickerOpt.DashboardOptions.UserName) && !string.IsNullOrEmpty(tickerOpt.DashboardOptions.Password))
                        {
                            dashboard.WithBasicAuth(tickerOpt.DashboardOptions.UserName, tickerOpt.DashboardOptions.Password);
                        }
                        else if (!string.IsNullOrEmpty(tickerOpt.DashboardOptions.WebApiKey))
                        {
                            dashboard.WithApiKey(tickerOpt.DashboardOptions.WebApiKey);
                        }
                    });
                }
            });

            // 注册后台服务，程序启动自动执行迁移、缺失表自动创建
            services.AddHostedService<TickerQAutoMigrateHostService>();

            return services;
        }
    }

    /// <summary>
    /// 程序启动自动执行TickerQ迁移，无表自动新建
    /// </summary>
    internal class TickerQAutoMigrateHostService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public TickerQAutoMigrateHostService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbCtx = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
            // 自动创建缺失表、增量更新表结构
            await dbCtx.Database.MigrateAsync(stoppingToken);
        }
    }
}