using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Viv.Clockwork;
using Viv.Clockwork.Enums;
using Viv.Contracts.Enums;
using Viv.Contracts.Interface;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Engine.HealthChecks;
using Viv.Engine.HostedServices;
using Viv.Engine.LocalEvents;
using Viv.Engine.Locking;
using Viv.Engine.Metrics;
using Viv.Engine.Options;
using Viv.Engine.UnitOfWork;
using Viv.Keke;
using Viv.Log;
using Viv.Momo;
using Viv.Momo.Core;
using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Nana;
using Viv.Nana.Core;
using Viv.Nana.Mqtt;
using Viv.Nana.Saga;
using Viv.Outbox;
using Viv.Redis;
using Viv.Redis.DbAllocator;
using Viv.Sandrone.Impl;
using Viv.Skirk;
using Wolverine.EntityFrameworkCore;

namespace Viv.Engine
{
    /// <summary>
    /// Viv 框架统一注册入口
    /// </summary>
    internal static class VivRegister
    {
        /// <summary>
        /// 注册所有Viv内部组件。
        /// </summary>
        public static void Register(IServiceCollection services, VivOptions options)
        {
            VivModules.RegisterAll(services, options);
        }

        #region 日志

        internal static void RegisterLogger(IServiceCollection services, VivOptions options)
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

        internal static void RegisterCache(IServiceCollection services, VivOptions options)
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

                // 预热：没有它，开机到第一次有人用缓存之间 /health 的 redis 项会报"配置未初始化"这个假故障
                services.AddHostedService<RedisPreheatHostedService>();
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

        internal static void RegisterNana(IServiceCollection services, VivOptions options)
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
            services.AddVivWolverine(options.NanaOption, enableSaga ? sagaTypes : null, [typeof(IVivOutbox)]);
            services.AddScoped<IVivEventPublisher, NanaEventPublisher>();
            // 本地事件发布器（进程内本地队列），与上面跨进程那条是平行的两条线，互不引用
            services.AddScoped<IVivLocalEventPublisher, NanaLocalEventPublisher>();
        }

        private static void RegisterSagaDbContext(IServiceCollection services, VivOptions options)
        {
            var nanaOpt = options.NanaOption;
            var connectionString = nanaOpt.SagaConnectionString!;

            // 必须是 AddDbContextWithWolverineIntegration，不能用普通 AddDbContext：
            // 只有它会把「这个 DbContext 里的实体」登记给 Wolverine 的 EF saga store。
            // 用 AddDbContext 时 Wolverine 认不出 EF 提供者，saga 会静默退回内存实现 ——
            // 流转一切正常、进程一重启状态全没，而且不报警（内存 saga 本来就是合法默认值）。
            //
            // 顺序：先注册 DbContext、再 UseWolverine（AddVivWolverine 里），这是 Wolverine 文档的顺序，
            // 现在的调用链（RegisterNana → RegisterSagaDbContext → AddVivWolverine）正好满足。
            services.AddDbContextWithWolverineIntegration<VivSagaDbContext>(dbOpt =>
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
            });

            // 建表交给启动钩子：每个 saga 类一张表、表结构跟着类走，没法手写 DDL。
            // 没有这一步，第一条 saga 消息进来会以「Invalid object name 'Saga_Xxx'」的形式落进死信。
            services.AddHostedService(sp => new VivSagaTableInitializer(
                sp.GetRequiredService<IServiceScopeFactory>(),
                nanaOpt,
                sp.GetRequiredService<ILoggerContract>()));
        }

        #endregion

        #region 数据库 Momo

        internal static void RegisterDatabase(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOption == null) return;

            services.AddScoped<IDatabaseOptionsProvider, DefaultDatabaseOptionsProvider>();
            services.AddScoped<IMomoDbContextFactory, MomoDbContextFactory>();
            services.AddScoped<IMomoDbContext, MomoDatabaseContext>();

            // 数据过滤器开关。实例无状态，状态在静态 AsyncLocal 里，与 IVivContextAccessor 同一形状。
            services.AddSingleton<IDataFilter, DataFilterSwitch>();

            // ── 工作单元（Unit of Work）─────────────────────────────
            // 内核适配器必须与 IMomoDbContext 同生命周期（都是 Scoped）：Momo 的事务状态
            // 挂在它自己的 _transaction 字段上、跟着实例走，适配器解析到别的实例就等于
            // 「开的和提交的是两个不同的事务」。
            services.AddScoped<ITransactionKernel>(sp => new MomoTransactionAdapter(sp.GetRequiredService<IMomoDbContext>()));
            services.AddScoped<IVivUnitOfWork, UnitOfWorkManager>();
        }

        #endregion

        #region 健康检查

        /// <summary>
        /// 给 /health 挂上真检查。原先只有 Aspire ServiceDefaults 那条恒 Healthy 的 self，
        /// Redis 挂了、库挂了 /health 照样绿，编排系统照样往里打流量。
        ///
        /// 两条都打 ready 不打 live：/alive 的谓词是 Tags.Contains("live")，所以进程活着就是活着，
        /// 依赖挂了不该让编排系统去重启它。AddHealthChecks 重复调用是安全的（TryAddSingleton + Configure 追加），
        /// 与 AddServiceDefaults 里那次不冲突。
        /// </summary>
        internal static void RegisterHealthChecks(IServiceCollection services, VivOptions options)
        {
            var builder = services.AddHealthChecks();

            if (options.CacheOption is { IsEnabled: true, CacheProviderType: DistributedCacheType.Redis })
            {
                builder.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
            }

            // 三个检查都要求「模块真的装上了」—— 配了节点但 IsEnabled=false 时对应客户端没注册，
            // 挂上去只会让 /health 恒为 Unhealthy，把「自己关掉的模块」报成故障
            if (options.DatabaseOption is { IsEnabled: true })
            {
                builder.AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
            }

            if (options.MqttOption is { IsEnabled: true })
            {
                builder.AddCheck<MqttHealthCheck>("mqtt", tags: ["ready"]);
            }

            if (options.NanaOption is { IsEnabled: true })
            {
                builder.AddCheck("rabbitmq", new RabbitMqHealthCheck(options.NanaOption), tags: ["ready"]);
            }
        }

        #endregion

        #region MQTT（设备接入）

        internal static void RegisterMqtt(IServiceCollection services, VivOptions options)
        {
            services.AddVivMqtt(options.MqttOption);
        }

        #endregion

        #region 异步任务 Skirk

        internal static void RegisterSkirk(IServiceCollection services, VivOptions options)
        {
            if (options.SkirkOption == null) return;

            // 任务表存在业务主库里：没配数据库（或数据库被 IsEnabled=false 关掉）既入不了队也执行不了，
            // 这里把话说死
            if (options.DatabaseOption is not { IsEnabled: true })
            {
                throw new Exception("SkirkOption 需要 DatabaseOption：异步任务表存在业务主库，缺少数据库配置无法工作");
            }

            VivAsyncTaskRegister.Initialize(services, options.SkirkOption);
        }

        #endregion

        #region 发件箱 Outbox

        internal static void RegisterOutbox(IServiceCollection services, VivOptions options)
        {
            if (options.OutboxOption == null) return;

            // 待发消息存在业务主库里，所以 DatabaseOption 两边都要。
            // MQ 则只有**投递**才需要 —— 入队走 ExecuteSqlAsync，全程不碰 MQ。
            // 故 NanaOption 的硬校验只对真的跑投递器的宿主成立：只写不投的宿主
            // （EnableDispatcher = false，把投递交给 Worker）配齐它等于平白多开一个 MQ 宿主，
            // 而跑投递器却没有 MQ 不是「降级运行」而是彻底不工作，照样在这里把话说死。
            if (options.OutboxOption.EnableDispatcher && options.NanaOption is not { IsEnabled: true })
            {
                throw new Exception("OutboxOption.EnableDispatcher 为 true 却没有启用 NanaOption：投递器要把待发消息发上 MQ，缺少 MQ 配置无法工作；本进程只写不投请配 EnableDispatcher = false");
            }

            if (options.DatabaseOption is not { IsEnabled: true })
            {
                throw new Exception("配置了 OutboxOption 却没有启用 DatabaseOption：发件箱要靠业务主库原子地存下待发消息");
            }

            OutboxRegister.Initialize(services, options.OutboxOption);
        }

        #endregion

        #region Inbox

        internal static void RegisterInbox(IServiceCollection services, VivOptions options)
        {
            if (options.DatabaseOption is not { IsEnabled: true }) return;
            InboxRegister.Initialize(services, options.InboxOption);
        }

        #endregion

        #region Token

        public static void RegisterToken(IServiceCollection services, VivOptions options)
        {
            // 这里永远注册 ITokenService，只是选实现：TokenOption 缺失或 IsEnabled=false
            // 都回落 NoneTokenService —— 不注册的话构造注入 ITokenService 的服务会解析失败
            if (options.TokenOption is { IsEnabled: true })
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

        internal static void RegisterScheduler(IServiceCollection services, VivOptions options)
        {
            if (options.TickOption == null) return;

            if (options.TickOption.SchedulerType == VivSchedulerType.TickerQ)
            {
                services.AddVivTickerQ(options.TickOption);
            }
        }

        /// <summary>
        /// 长连接的共享部分。Hub 是泛型，这里只能配 SignalR 的进程级选项；
        /// 每个 Hub 的会话由宿主 AddKinna&lt;THub&gt;() 注册。
        /// </summary>
        internal static void RegisterKinna(IServiceCollection services, VivOptions options)
        {
            if (options.KinnaOption == null) return;

            services.AddSignalR(o => o.EnableDetailedErrors = options.KinnaOption.EnableDetailedErrors);
        }

        #endregion

        #region 跨服务通信 Keke

        internal static void RegisterKeke(IServiceCollection services, VivOptions options)
        {
            if (options.KekeOption == null) return;
            KekeRegister.Initialize(services, options.KekeOption);
        }

        #endregion

        #region 其他服务注册

        internal static void RegisterOtherServices(IServiceCollection services, VivOptions options)
        {
            // 单例：无可变状态；Agent 是长生命周期对象，可能在后台线程/流式回调里取用，
            // Scoped 在那个时机已经释放（实测过：Scoped 会在后台场景随机解析失败）
            services.AddSingleton<IAiClientFactory, AiClientFactory>();
        }

        /// <summary>
        /// 对象存储。与其它子系统同姿态：配了节点才装。
        ///
        /// 不配也照样能注入 <see cref="IS3Service"/> 的话，失败点会推到「第一次真的上传文件」那一刻，
        /// 离根因（没配 S3Option）隔着好几层调用栈。解析不到就是没配，更早也更准。
        /// 真要在没配 S3 的宿主里用，走 <c>GetService</c> 拿可空实例，别用构造注入。
        /// </summary>
        internal static void RegisterS3(IServiceCollection services, VivOptions options)
        {
            services.AddSingleton<IS3Service, VivS3Service>();
        }

        #endregion
    }
}