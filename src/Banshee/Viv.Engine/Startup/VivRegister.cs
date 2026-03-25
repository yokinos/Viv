using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Engine.Cache;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Log.Enums;
using Viv.Log.VivLogger;
using Viv.Momo;
using Viv.Momo.Core;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Redis;
using Viv.Vva.Extension;

namespace Viv.Engine.Startup
{
    /// <summary>
    /// Viv 框架统一注册入口
    /// </summary>
    internal static class VivRegister
    {
        /// <summary>
        /// 注册所有Viv内部组件
        /// </summary>
        public static void Register(IServiceCollection services, VivOptions options)
        {
            // 注册Viv上下文
            services.AddScoped<IVivContext, VivContext>();

            // 注册日志
            RegisterLogger(services, options);
            // 注册缓存
            RegisterCache(services, options);
            // 注册消息队列
            RegisterNana(services, options);
            // 注册数据库
            RegisterDatabase(services, options);
        }

        #region 日志

        private static void RegisterLogger(IServiceCollection services, VivOptions options)
        {
            if (options.LogOptions == null) return;

            VivLogFactory.Initialize(options.LogOptions);
            services.AddSingleton<IVivLogger>(provider =>
            {
                return options.LogOptions.LoggerType switch
                {
                    LoggerType.None => new NoneLogger(),
                    LoggerType.Log4net => new Log4netLogger(),
                    LoggerType.NLog => new NLogLogger(),
                    LoggerType.Serilog => new SerilogLogger(),
                    _ => new NoneLogger()
                };
            });
        }

        #endregion

        #region 缓存

        private static void RegisterCache(IServiceCollection services, VivOptions options)
        {
            if (options.CacheOptions == null) return;

            // Redis 缓存
            if (options.CacheOptions.CacheProviderType == Enums.DistributedCacheType.Redis)
            {
                RedisFactory.Initialize(options.CacheOptions.RedisOptions);
                services.AddSingleton<IRedisService, RedisService>();
            }

            // 内存缓存
            if (options.CacheOptions.IsEnableMemoryCache)
            {
                services.AddMemoryCache();
                services.AddSingleton<IMemoryCacheService, MemoryCacheService>();
            }
        }

        #endregion

        #region 消息队列 Nana

        private static void RegisterNana(IServiceCollection services, VivOptions options)
        {
            if (options.NanaOptions == null) return;

            NanaRegister.Initialize(options.NanaOptions);
            services.AddSingleton<IVivProducer, NanaProducer>();

            // 注册消费者
            if (!options.NanaOptions.ConsumerTypes.IsNullOrEmpty())
            {
                services.AddSingleton(new NanaConsumerHostedService(options.NanaOptions.ConsumerTypes));
                services.AddHostedService(sp => sp.GetRequiredService<NanaConsumerHostedService>());
            }
        }

        #endregion

        #region 数据库 Momo

        private static void RegisterDatabase(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOptions == null) return;

            MomoRegister.Initialize(options.DatabaseOptions);
            services.AddScoped<IVivDbContext, VivDatabaseContext>();
        }

        #endregion
    }
}