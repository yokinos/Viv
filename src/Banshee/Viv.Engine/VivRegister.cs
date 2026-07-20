using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Viv.Authentication;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Echo;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Core;
using Viv.Momo.Enums;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Nana.Saga;
using Viv.Redis;
using Viv.Sandrone.Impl;
using Viv.Tick;
using Viv.Tick.Enums;

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
            // 注册跨服务通信（HTTP + gRPC）
            RegisterEcho(services, options);
            // 注册缓存
            RegisterCache(services, options);
            // 注册消息队列
            RegisterNana(services, options);
            // 注册数据库
            RegisterDatabase(services, options);
            // 注册Token
            RegisterToken(services, options);
            // 注册调度
            RegisterScheduler(services, options);
            // 注册其他服务
            RegisterOtherServices(services, options);
        }

        #region 日志

        private static void RegisterLogger(IServiceCollection services, VivOptions options)
        {
            if (options.LogOption == null) return;
            LoggerRegister.Initialize(options.LogOption);
            if (options.LogOption.LogType == LogType.Serilog)
            {
                SerilogProvider.Initialize();
                services.AddSingleton<ILoggerContract, SerilogLoggerImpl>();
            }
            else
            {
                services.AddSingleton<ILoggerContract, NoneLoggerImpl>();
            }
        }

        #endregion

        #region 缓存

        private static void RegisterCache(IServiceCollection services, VivOptions options)
        {
            if (options.CacheOption == null) return;

            // Redis 缓存
            if (options.CacheOption.CacheProviderType == DistributedCacheType.Redis)
            {
                RedisFactory.Initialize(options.CacheOption.RedisOptions);
                services.AddSingleton<IRedisService, RedisService>();

                // 将 IConnectionMultiplexer 注册到 DI，供 OpenTelemetry Redis 仪表板使用
                services.AddSingleton<IConnectionMultiplexer>(
                    RedisFactory.GetConnectionAsync().GetAwaiter().GetResult());
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

            // 扫描 IVivSagaStateMachine — 有实现且配了 SagaConnectionString 才启用
            var sagaTypes = TypeScanMagic.ScanTypes<IVivSagaStateMachine>();
            var enableSaga = options.NanaOption.SagaConnectionString is not null && !sagaTypes.IsNullOrEmpty();

            if (enableSaga)
            {
                RegisterSagaDbContext(services, options, sagaTypes);
            }

            // 注册 MassTransit + RabbitMQ（Saga 类型传进去）
            services.AddVivMassTransit(options.NanaOption, enableSaga ? sagaTypes : null);

            services.AddScoped<IVivEventPublisher, NanaEventPublisher>();
        }

        private static void RegisterSagaDbContext(IServiceCollection services, VivOptions options, List<Type> sagaStateMachineTypes)
        {
            var nanaOpt = options.NanaOption;
            var connectionString = nanaOpt.SagaConnectionString!;

            services.AddDbContext<VivSagaDbContext>(dbOpt =>
            {
                switch (nanaOpt.SagaDatabaseSource)
                {
                    case DatabaseSourceType.PostgreSQL:
                        dbOpt.UseNpgsql(connectionString);
                        break;
                    case DatabaseSourceType.SqlServer:
                        dbOpt.UseSqlServer(connectionString);
                        break;
                    default:
                        throw new NotSupportedException($"Saga 不支持该数据库类型：{nanaOpt.SagaDatabaseSource}");
                }
            }, contextLifetime: ServiceLifetime.Scoped);

            // 扫描所有 VivSagaClassMap 实现，注入到 VivSagaDbContext
            var classMapTypes = TypeScanMagic.ScanTypes<ISagaClassMap>();
            foreach (var t in classMapTypes)
            {
                services.AddScoped(typeof(ISagaClassMap), t);
            }
        }

        #endregion

        #region 数据库 Momo

        private static void RegisterDatabase(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOption == null) return;

            MomoRegister.Initialize(options.DatabaseOption);
            services.AddScoped<IMomoDbContext, MomoDatabaseContext>();
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

        #region 调度框架

        private static void RegisterScheduler(IServiceCollection services, VivOptions options)
        {
            if (options.TickOption == null) return;

            if (options.TickOption.SchedulerType == VivSchedulerType.TickerQ)
            {
                services.AddVivTickerQ(options.TickOption);
            }
        }

        #endregion

        #region 跨服务通信 Echo

        private static void RegisterEcho(IServiceCollection services, VivOptions options)
        {
            if (options.EchoOption == null) return;
            EchoRegister.Initialize(services, options.EchoOption);
        }

        #endregion

        #region 其他服务注册

        public static void RegisterOtherServices(IServiceCollection services, VivOptions options)
        {
            if (options.OpenAIOption != null)
            {
                VivConfigRegistry.Add(options.OpenAIOption);
            }

            services.AddScoped<IAiClientFactory, AiClientFactory>();
        }

        #endregion
    }
}