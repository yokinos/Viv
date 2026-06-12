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

            var descriptors = TickRegister.CollectPendingTasks();

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

                foreach (var desc in descriptors)
                {
                    MapTaskViaReflection(opt, desc);
                }
            });

            return services;
        }

        /// <summary>
        /// 通过反射调用 opt.MapTicker&lt;T&gt;(cfg => cfg.SetCron(cron))
        /// </summary>
        private static void MapTaskViaReflection(object builder, TickerQTaskDescriptor desc)
        {
            // 找 ITickerQBuilder.MapTicker<T>(Action<TickerConfigurator<T>>) 泛型方法
            var mapMethod = builder.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "MapTicker" && m.IsGenericMethodDefinition);
            if (mapMethod == null) return;

            var genericMap = mapMethod.MakeGenericMethod(desc.TaskType);

            // 参数类型：Action<TickerConfigurator<T>>
            var actionType = genericMap.GetParameters()[0].ParameterType;
            var configType = actionType.GenericTypeArguments[0];

            // 构造: configurator => configurator.SetCron(cron)
            var cfgParam = Expression.Parameter(configType, "cfg");
            var setCron = configType.GetMethod("SetCron", [typeof(string)]);
            if (setCron == null) return;

            var body = Expression.Call(cfgParam, setCron, Expression.Constant(desc.Cron));
            var lambda = Expression.Lambda(actionType, body, cfgParam);

            genericMap.Invoke(builder, [lambda.Compile()]);
        }
    }
}
