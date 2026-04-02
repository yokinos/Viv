using Microsoft.Extensions.DependencyInjection;
using Viv.Authentication;
using Viv.Contracts.Interface;
using Viv.Engine.Cache;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Core;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Redis;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Engine
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
            // 注册Token
            RegisterToken(services, options);
        }

        #region 日志

        private static void RegisterLogger(IServiceCollection services, VivOptions options)
        {
            if (options.LogOption == null) return;
            LoggerRegister.Initialize(options.LogOption);
            if (options.LogOption.LogType == LogType.Serilog)
            {
                services.AddSingleton<IDistributedLogger, SerilogDistributedLogger>();
            }
            else
            {
                services.AddSingleton<IDistributedLogger, NoneLogger>();
            }
        }

        #endregion

        #region 缓存

        private static void RegisterCache(IServiceCollection services, VivOptions options)
        {
            if (options.CacheOption == null) return;

            // Redis 缓存
            if (options.CacheOption.CacheProviderType == Enums.DistributedCacheType.Redis)
            {
                RedisFactory.Initialize(options.CacheOption.RedisOptions);
                services.AddSingleton<IRedisService, RedisService>();
            }

            // 内存缓存
            if (options.CacheOption.IsEnableMemoryCache)
            {
                services.AddMemoryCache();
                services.AddSingleton<IMemoryCacheService, MemoryCacheService>();
            }
        }

        #endregion

        #region 消息队列 Nana

        private static void RegisterNana(IServiceCollection services, VivOptions options)
        {
            if (options.NanaOption == null) return;

            NanaRegister.Initialize(options.NanaOption);
            services.AddSingleton<IVivProducer, NanaProducer>();

            // 注册消费者
            if (!options.NanaOption.ConsumerTypes.IsNullOrEmpty())
            {
                services.AddSingleton(new NanaConsumerHostedService(options.NanaOption.ConsumerTypes));
                services.AddHostedService(sp => sp.GetRequiredService<NanaConsumerHostedService>());
            }
        }

        #endregion

        #region 数据库 Momo

        private static void RegisterDatabase(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOption == null) return;

            MomoRegister.Initialize(options.DatabaseOption);
            services.AddScoped<IVivDbContext, VivDatabaseContext>();
        }

        #endregion

        #region Token

        public static void RegisterToken(IServiceCollection services, VivOptions options)
        {
            if (options.TokenOption != null)
            {
                // 注册token实现
                services.AddScoped<ITokenService, JwtTokenService>();
                VivConfigRegistry.Add(options.TokenOption);
            }
        }

        #endregion
    }
}