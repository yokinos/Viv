using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Viv.Clockwork;
using Viv.Clockwork.Enums;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Echo;
using Viv.Engine.LocalEvents;
using Viv.Engine.Options;
using Viv.Engine.UnitOfWork;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Core;
using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Nana.Saga;
using Viv.Outbox;
using Viv.Redis;
using Viv.Redis.DbAllocator;
using Viv.Sandrone.Impl;

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
            services.AddSingleton<IVivContextProvider, DefaultVivContextProvider>();
            services.AddSingleton<IVivContextAccessor, VivContextAccessor>();
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
            // 注册发件箱（依赖数据库 + MQ；内部会注册 IHostedService 投递器）
            RegisterOutbox(services, options);
            // 可选 Inbox（有数据库就注册，消费者自行决定是否用）
            RegisterInbox(services, options);
            // 注册Token
            RegisterToken(services, options);
            // 注册调度
            RegisterScheduler(services, options);
            // 注册本地事件总线（扫描 IVivLocalEventHandler<> 处理器，与 Nana 完全解耦）
            LocalEventRegistration.Register(services);
            // 注册其他服务
            RegisterOtherServices(services, options);
        }

        #region 日志

        private static void RegisterLogger(IServiceCollection services, VivOptions options)
        {
            if (options.LogOption == null) return;

            if (options.LogOption.IsUseSeq && options.LogOption.SeqUrl.IsNullOrEmpty())
            {
                throw new Exception("Seq地址不能为空");
            }

            if (options.LogOption.LogType == LogType.Serilog)
            {
                SerilogProvider.Initialize(options.LogOption);
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
                switch (options.CacheOption.RedisOptions.SelectorType)
                {
                    case DbSelectorType.KeyHash:
                        services.AddSingleton<IDbAllocator, KeyHashAllocator>();
                        break;
                    case DbSelectorType.TenantIdHash:
                        services.AddSingleton<IDbAllocator, TenantIdAllocator>();
                        break;
                    case DbSelectorType.None:
                        services.AddSingleton<IDbAllocator, NoneAllocator>();
                        break;
                }

                services.AddSingleton<IRedisService, RedisService>();
                services.AddSingleton<IDistributedLock, DistributedLockAccessor>();

                // 将 IConnectionMultiplexer 注册到 DI，供 OpenTelemetry Redis 仪表板使用
                // services.AddSingleton(RedisFactory.GetConnectionAsync().GetAwaiter().GetResult());
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

            // 业务 Core 程序集（含 Saga 类型）可能是懒加载，先强制加载传递引用再扫描，
            // 否则 ScanTypes<VivSagaState>() 只看到已加载程序集，Saga 会被静默跳过。
            TypeScanMagic.ForceLoadReferencedAssemblies();

            // 扫描 VivSagaState 子类 — 配了 SagaConnectionString 才启用 EF Saga 持久化
            var sagaTypes = TypeScanMagic.ScanTypes<VivSagaState>();
            var enableSaga = options.NanaOption.SagaConnectionString is not null && !sagaTypes.IsNullOrEmpty();

            if (enableSaga)
            {
                RegisterSagaDbContext(services, options);
            }

            // 注册 Wolverine + RabbitMQ（Saga 类型传进去；VivWolverineConfigurationExtensions 内部含队列路由/失败策略）
            services.AddVivWolverine(options.NanaOption, enableSaga ? sagaTypes : null);
            services.AddScoped<IVivEventPublisher, NanaEventPublisher>();
            // 本地事件发布器（进程内本地队列），与上面跨进程那条是平行的两条线，互不引用
            services.AddScoped<IVivLocalEventPublisher, NanaLocalEventPublisher>();
        }

        private static void RegisterSagaDbContext(IServiceCollection services, VivOptions options)
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
        }

        #endregion

        #region 数据库 Momo

        private static void RegisterDatabase(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOption == null) return;

            services.AddScoped<IDatabaseOptionsProvider, DefaultDatabaseOptionsProvider>();
            services.AddScoped<IMomoDbContext, MomoDatabaseContext>();

            // 数据过滤器开关。实例无状态，状态在静态 AsyncLocal 里，与 IVivContextAccessor 同一形状。
            services.AddSingleton<IDataFilter, DataFilterSwitch>();

            // ── 工作单元（Unit of Work）─────────────────────────────
            // 内核适配器必须与 IMomoDbContext 同生命周期（都是 Scoped）：Momo 的事务状态
            // 挂在它自己的 _transaction 字段上、跟着实例走，适配器解析到别的实例就等于
            // 「开的和提交的是两个不同的事务」。
            services.AddScoped<ITransactionKernel>(sp =>
                new MomoTransactionAdapter(sp.GetRequiredService<IMomoDbContext>()));
            services.AddScoped<IVivUnitOfWork, UnitOfWorkManager>();
        }

        #endregion

        #region 发件箱 Outbox

        private static void RegisterOutbox(IServiceCollection services, VivOptions options)
        {
            if (options.OutboxOption == null) return;

            // 发件箱投的是 NanaEvent（复用跨进程那族的 fanout 拓扑），且待发消息存在业务主库里。
            // 缺任何一边都不是「降级运行」而是彻底不工作，所以在这里就把话说死 ——
            // 否则表现成投递时 IVivEventPublisher 解析不到，或者表根本不存在。
            if (options.NanaOption == null)
            {
                throw new Exception("配置了 OutboxOption 却没有 NanaOption：发件箱投递的是跨进程事件，缺少 MQ 配置无法工作");
            }

            if (options.DatabaseOption == null)
            {
                throw new Exception("配置了 OutboxOption 却没有 DatabaseOption：发件箱要靠业务主库原子地存下待发消息");
            }

            OutboxRegister.Initialize(services, options.OutboxOption);
        }

        #endregion

        #region Inbox

        private static void RegisterInbox(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOption == null) return;
            InboxRegister.Initialize(services, options.InboxOption);
        }

        #endregion

        #region Token

        public static void RegisterToken(IServiceCollection services, VivOptions options)
        {
            if (options.TokenOption != null)
            {
                // 注册token实现
                services.AddScoped<ITokenService, JwtTokenService>();
            }
            else
            {
                services.AddScoped<ITokenService, NoneTokenService>();
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
            services.AddScoped<IAiClientFactory, AiClientFactory>();
            services.AddSingleton<IS3Service, VivS3Service>();
        }

        #endregion
    }
}