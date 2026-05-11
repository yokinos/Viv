using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;

namespace Viv.Sayu
{
    public static class ScheduleExtensions
    {
        public static IServiceCollection AddTickerQSchedule(this IServiceCollection services, TickerQOptions options)
        {
            services.AddTickerQ(opt =>
            {
                if (options.EnableDashboard)
                {
                    opt.AddDashboard(dashboard =>
                    {
                        dashboard.SetBasePath(options.DashboardPath);
                    });
                }
            });

            return services;
        }
    }
}
