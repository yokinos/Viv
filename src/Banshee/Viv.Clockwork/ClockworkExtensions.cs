using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Interfaces;
using Viv.Clockwork.Options;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Momo.Enums;

namespace Viv.Clockwork
{
    /// <summary>
    /// TickerQ 接入。定时任务没有 MVC 过滤器 / 消费者 HandleAsync 那样的本地事件触发点，
    /// 若任务里会 <c>IVivLocalEventBus.PublishAsync</c>，请用 <c>IVivLocalEventScope.RunAsync</c>
    /// 包住业务（成功 Flush，失败 Discard），并在清理 <c>IVivContext</c> 之前完成分发。
    /// </summary>
    public static class ClockworkExtensions
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
                        else if (!string.IsNullOrEmpty(tickerOpt.DashboardOptions.WebApiKey))
                        {
                            dashboard.WithApiKey(tickerOpt.DashboardOptions.WebApiKey);
                        }
                    });
                }
            });

            return services;
        }
    }
}