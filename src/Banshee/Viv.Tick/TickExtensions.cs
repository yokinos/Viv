using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using System.Reflection;
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
                if (!tickerOpt.ConnectionString.IsNullOrEmpty())
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
                        if (!tickerOpt.DashboardOptions.UserName.IsNullOrEmpty() && !tickerOpt.DashboardOptions.Password.IsNullOrEmpty())
                        {
                            dashboard.WithBasicAuth(tickerOpt.DashboardOptions.UserName, tickerOpt.DashboardOptions.Password);
                        }
                        else if (!tickerOpt.DashboardOptions.WebApiKey.IsNullOrEmpty())
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
