using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DbContextFactory;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Interfaces;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
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
                                case DatabaseSourceType.PostgreSQL:
                                    dbOpt.UseNpgsql(tickerOpt.ConnectionString, sql => sql.MigrationsAssembly(tickerOpt.AssemblyName));
                                    break;
                                case DatabaseSourceType.SqlServer:
                                    dbOpt.UseSqlServer(tickerOpt.ConnectionString, sql => sql.MigrationsAssembly(tickerOpt.AssemblyName));
                                    break;
                                default:
                                    throw new NotSupportedException($"TickerQ 暂不支持数据库类型: {tickerOpt.DatabaseType}");
                            }
                        }, schema: tickerOpt.EFCoreSchemaName);
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