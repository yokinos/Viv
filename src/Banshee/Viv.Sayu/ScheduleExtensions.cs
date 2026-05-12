using Microsoft.Extensions.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using Viv.Momo.Enums;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Sayu
{
    public static class ScheduleExtensions
    {
        public static IServiceCollection AddVivTickerQ(this IServiceCollection services, SayuOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.TickerQ);

            var tickerOpt = options.TickerQ;

            //services.AddTickerQ(opt =>
            //{
            //    opt.SetMaxConcurrency(tickerOpt.MaxConcurrency);

            //    if (!tickerOpt.ConnectionString.IsNullOrEmpty())
            //    {
            //        opt.AddOperationalStore(efOpt =>
            //        {
            //            efOpt.UseTickerQDbContext<>(dbOpt =>
            //            {
            //                switch (tickerOpt.DatabaseType)
            //                {
            //                    case DatabaseSouceType.PostgreSQL:
            //                        dbOpt.UseNpgsql(tickerOpt.ConnectionString);
            //                        break;
            //                    case DatabaseSouceType.SqlServer:
            //                        dbOpt.UseSqlServer(tickerOpt.ConnectionString);
            //                        break;
            //                    default:
            //                        throw new NotSupportedException($"TickerQ 暂不支持数据库类型: {tickerOpt.DatabaseType}");
            //                }
            //            });
            //        });
            //    }

            //    if (tickerOpt.EnableDashboard)
            //    {
            //        opt.AddDashboard(dashboard =>
            //        {
            //            dashboard.SetBasePath(tickerOpt.DashboardPath);
            //        });
            //    }
            //});

            return services;
        }
    }
}
