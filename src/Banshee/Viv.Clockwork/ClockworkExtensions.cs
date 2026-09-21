using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using Viv.Clockwork.Options;
using Viv.Momo.Enums;

namespace Viv.Clockwork
{
    /// <summary>
    /// TickerQ 接入。任务入口请继承 <see cref="VivTickerJobBase"/>，经 <c>RunAsync</c> 默认包上
    /// <c>IVivLocalEventScope</c>（成功 Flush，失败 Discard），开工前必须 SetSnapshot。
    /// Dashboard 打开却没有凭证会启动失败。
    /// </summary>
    public static class ClockworkExtensions
    {
        public static IServiceCollection AddVivTickerQ(this IServiceCollection services, TickOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.TickerQ);
            var tickerOpt = options.TickerQ;

            if (tickerOpt.EnableDashboard)
            {
                EnsureDashboardCredentials(tickerOpt);
            }

            services.AddTickerQ(opt =>
            {
                if (!string.IsNullOrEmpty(tickerOpt.ConnectionString))
                {
                    opt.AddOperationalStore(efOpt =>
                    {
                        efOpt.UseTickerQDbContext<TickerQDbContext>(dbOpt =>
                        {
                            switch (tickerOpt.DatabaseSource)
                            {
                                case DatabaseSourceType.PostgreSQL:
                                    dbOpt.UseNpgsql(tickerOpt.ConnectionString, sql => sql.MigrationsAssembly(tickerOpt.AssemblyName));
                                    break;
                                case DatabaseSourceType.SqlServer:
                                    dbOpt.UseSqlServer(tickerOpt.ConnectionString, sql => sql.MigrationsAssembly(tickerOpt.AssemblyName));
                                    break;
                                default:
                                    throw new NotSupportedException($"TickerQ 暂不支持数据库类型: {tickerOpt.DatabaseSource}");
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
                        else
                        {
                            dashboard.WithApiKey(tickerOpt.DashboardOptions.WebApiKey);
                        }
                    });
                }
            });

            return services;
        }

        internal static void EnsureDashboardCredentials(TickerQOptions tickerOpt)
        {
            var dash = tickerOpt.DashboardOptions ?? new TickerQDashboradOptions();
            var hasBasic = !string.IsNullOrWhiteSpace(dash.UserName) && !string.IsNullOrWhiteSpace(dash.Password);
            var hasApiKey = !string.IsNullOrWhiteSpace(dash.WebApiKey);
            if (!hasBasic && !hasApiKey)
            {
                throw new InvalidOperationException(
                    "TickerQ Dashboard 已启用但未配置凭证。请设置 DashboardOptions.UserName/Password 或 WebApiKey。");
            }
        }
    }
}
