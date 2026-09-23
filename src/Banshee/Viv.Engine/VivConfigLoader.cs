using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Viv.Aoi;
using Viv.Clockwork.Options;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Echo;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Outbox.Options;
using Viv.Redis;

namespace Viv.Engine
{
    /// <summary>
    /// Viv 框架配置加载器 —— 全进程唯一的 VivOptions 绑定入口。
    ///
    /// 绑一次、快照一次、注册一次。以前绑定散在三个 starter（各自 LoadVivConfig + AddVivConfig 各绑一遍）
    /// 加 VivEngine 自己一份深拷贝，同一份 appsettings.json 会得到三份对象图，改动只落在其中一份上。
    ///
    /// 提供两种互斥模式，二选一，不要同时调用：
    /// 1. AddVivConfig（推荐）：静态实例模式，绑定后注册为 Singleton 实例 + IOptions&lt;T&gt;
    /// 2. AddVivConfigFromConfiguration：动态配置模式，使用 Configure + IOptionsMonitor，支持热更新
    ///
    /// 两种模式都返回绑定出来的那份 VivOptions，且都写入 VivEngine.VivOptions 静态快照。
    /// 区别只在 DI 那侧：动态模式的热更新改的是 IOptionsMonitor 里的值，静态快照是启动时冻结的
    /// —— 走静态读写的那几处（RequestTokenResolver / 网关判定 / ApiStartedMiddleware）看不到刷新。
    /// </summary>
    public static class VivConfigLoader
    {
        /// <summary>
        /// 绑定 IConfiguration 的 VivOptions 节点，写入全局静态快照并返回。
        /// 节点缺失时返回一份默认实例（不返回 null）。
        ///
        /// 有副作用：会覆盖 VivEngine.VivOptions 与 VivAppStartTime。测试要单独拿配置也走它，
        /// 免得在别处再写一份绑定表达式 —— 那正是这一轮要消掉的东西。
        /// </summary>
        public static VivOptions Load(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var options = configuration.GetSection(nameof(VivOptions)).Get<VivOptions>() ?? new VivOptions();
            VivEngine.SetVivOptions(options);
            return options;
        }

        /// <summary>
        /// 【推荐】从 IConfiguration 的 VivOptions 节点加载配置并注册为静态实例
        /// 适合：配置在启动时固定，不需要热更新的场景
        /// 注入方式：直接注入 T，或注入 IOptions&lt;T&gt;
        /// 注意：如果某个子配置节点为 null，则不会注册到 DI，注入该类型时会报错
        /// </summary>
        public static VivOptions AddVivConfig(this IHostApplicationBuilder builder)
        {
            var options = Load(builder.Configuration);
            RegisterOptions(builder.Services, options);
            return options;
        }

        /// <summary>
        /// 从 IConfiguration 绑定 VivOptions（使用 Configure + IOptionsMonitor，支持热更新）
        /// 适合：配置需要动态刷新、多环境覆盖的场景
        /// 注入方式：只能注入 IOptionsMonitor&lt;T&gt; / IOptionsSnapshot&lt;T&gt; / IOptions&lt;T&gt;
        ///          不能直接注入 T（本方法不注册 T 本身）
        /// 注意：节点为 null 时 Configure 会生成默认对象，注入不会失败，但值都是默认值
        /// </summary>
        public static VivOptions AddVivConfigFromConfiguration(this IHostApplicationBuilder builder)
        {
            var services = builder.Services;
            var configuration = builder.Configuration;

            var options = Load(configuration);

            services.Configure<VivOptions>(configuration.GetSection("VivOptions"));
            services.Configure<EnvOptions>(configuration.GetSection("VivOptions:EnvOption"));
            services.Configure<DIOptions>(configuration.GetSection("VivOptions:DIOption"));
            services.Configure<VivCacheOptions>(configuration.GetSection("VivOptions:CacheOption"));
            services.Configure<RedisOptions>(configuration.GetSection("VivOptions:CacheOption:RedisOptions"));
            services.Configure<LogOptions>(configuration.GetSection("VivOptions:LogOption"));
            services.Configure<DatabaseOptions>(configuration.GetSection("VivOptions:DatabaseOption"));
            services.Configure<NanaOptions>(configuration.GetSection("VivOptions:NanaOption"));
            services.Configure<OutboxOptions>(configuration.GetSection("VivOptions:OutboxOption"));
            services.Configure<InboxOptions>(configuration.GetSection("VivOptions:InboxOption"));
            services.Configure<TokenOptions>(configuration.GetSection("VivOptions:TokenOption"));
            services.Configure<TickOptions>(configuration.GetSection("VivOptions:TickOption"));
            services.Configure<TickerQOptions>(configuration.GetSection("VivOptions:TickOption:TickerQ"));
            services.Configure<EchoOptions>(configuration.GetSection("VivOptions:EchoOption"));
            services.Configure<GrpcOptions>(configuration.GetSection("VivOptions:EchoOption:GrpcOption"));
            services.Configure<CorsOptions>(configuration.GetSection("VivOptions:CorsOption"));
            services.Configure<OpenAIOptions>(configuration.GetSection("VivOptions:OpenAIOption"));
            services.Configure<S3Options>(configuration.GetSection("VivOptions:S3Option"));

            // VivInternalTokenOptions 与静态模式保持一致，从 EnvOption 派生
            services.AddSingleton(sp =>
            {
                var env = sp.GetRequiredService<IOptions<EnvOptions>>().Value;
                return new VivInternalTokenOptions
                {
                    InternalToken = env.InternalToken,
                    ServiceName = env.ServiceName
                };
            });

            return options;
        }

        /// <summary>
        /// 将非 null 的配置实例注册到 DI 容器（T + IOptions&lt;T&gt;）
        /// </summary>
        private static void RegisterOptions(IServiceCollection services, VivOptions options)
        {
            // 主配置
            RegisterOption(services, options);

            // EnvOption
            if (options.EnvOption != null)
            {
                RegisterOption(services, options.EnvOption);
            }

            // VivInternalTokenOptions 由 EnvOption 派生，恒注册（EnvOption 缺失时给空对象）：
            // Echo gRPC 拦截器构造注入它，未注册会直接导致 gRPC 端激活失败
            RegisterOption(services, new VivInternalTokenOptions
            {
                InternalToken = options.EnvOption?.InternalToken,
                ServiceName = options.EnvOption?.ServiceName
            });

            // DIOption
            if (options.DIOption != null)
                RegisterOption(services, options.DIOption);

            // CacheOption
            if (options.CacheOption != null)
            {
                RegisterOption(services, options.CacheOption);

                if (options.CacheOption.RedisOptions != null)
                    RegisterOption(services, options.CacheOption.RedisOptions);
            }

            // LogOption
            if (options.LogOption != null)
                RegisterOption(services, options.LogOption);

            // DatabaseOption
            if (options.DatabaseOption != null)
                RegisterOption(services, options.DatabaseOption);

            // NanaOption
            if (options.NanaOption != null)
                RegisterOption(services, options.NanaOption);

            // OutboxOption
            if (options.OutboxOption != null)
                RegisterOption(services, options.OutboxOption);

            // InboxOption —— 与别的子配置不同，这个节点允许缺席：
            // Inbox 的启用条件是「配了 DatabaseOption」。缺席时不在这里注册，
            // 由 InboxRegister 用默认值兜底，否则清理器解析不到配置会直接把宿主拖垮。
            if (options.InboxOption != null)
                RegisterOption(services, options.InboxOption);

            // TokenOption
            if (options.TokenOption != null)
                RegisterOption(services, options.TokenOption);

            // TickOption
            if (options.TickOption != null)
            {
                RegisterOption(services, options.TickOption);

                if (options.TickOption.TickerQ != null)
                    RegisterOption(services, options.TickOption.TickerQ);
            }

            // EchoOption
            if (options.EchoOption != null)
            {
                RegisterOption(services, options.EchoOption);

                if (options.EchoOption.GrpcOption != null)
                    RegisterOption(services, options.EchoOption.GrpcOption);
            }

            // CorsOption
            if (options.CorsOption != null)
                RegisterOption(services, options.CorsOption);

            // OpenAIOption
            if (options.OpenAIOption != null)
                RegisterOption(services, options.OpenAIOption);

            // S3Option
            if (options.S3Option != null)
                RegisterOption(services, options.S3Option);
        }

        /// <summary>
        /// 将单个配置实例注册到 DI 容器（静态实例模式）
        /// 同时注册为 T 和 IOptions&lt;T&gt;
        /// </summary>
        private static void RegisterOption<T>(IServiceCollection services, T value) where T : class
        {
            services.AddSingleton(value);
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(value));
        }
    }
}