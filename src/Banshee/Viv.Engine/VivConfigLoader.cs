using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Viv.Aoi;
using Viv.Clockwork.Options;
using Viv.Contracts.Options;
using Viv.Echo;
using Viv.Engine.Options;
using Viv.Log;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Redis;

namespace Viv.Engine
{
    /// <summary>
    /// Viv 框架配置加载器
    /// 从 appsettings.json 的 VivOptions 节点加载配置，并注册到 DI
    /// 支持两种模式：
    /// 1. 静态实例模式（默认）：绑定后注册为 Singleton 实例，配置为 null 则不注入
    /// 2. 动态配置模式（可选）：使用 Configure + IOptionsMonitor 支持热更新
    /// </summary>
    public static class VivConfigLoader
    {
        /// <summary>
        /// 从 IConfiguration 的 VivOptions 节点加载配置并注册为静态实例
        /// 适合：配置在启动时固定，不需要热更新的场景
        /// 注意：如果某个配置节点为 null，则不会注册到 DI，注入时会报错
        /// </summary>
        public static IServiceCollection AddVivConfig(this IServiceCollection services, IConfiguration configuration)
        {
            // 1. 从配置节点绑定出完整对象（子节点为 null 则保持 null）
            var options = new VivOptions();
            configuration.GetSection(nameof(VivOptions)).Bind(options);

            // 2. 注册所有配置，为 null 的子配置跳过
            RegisterOptions(services, options);

            return services;
        }

        /// <summary>
        /// 从 IConfiguration 绑定 VivOptions（使用 Configure + IOptionsMonitor，支持热更新）
        /// 适合：配置需要动态刷新、多环境覆盖的场景
        /// 注意：此方式不会注册 T 的直接注入，只能注入 IOptionsMonitor&lt;T&gt; / IOptionsSnapshot&lt;T&gt;
        ///       如果节点为 null，Configure 会使用默认值（通常为 null），不会导致注册失败，但注入时可能为 null
        /// </summary>
        public static IServiceCollection AddVivConfigFromConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<VivOptions>(configuration.GetSection(nameof(VivOptions)));
            services.Configure<EnvOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(EnvOptions)}"));
            services.Configure<VivInternalTokenOptions>(configuration.GetSection(nameof(VivInternalTokenOptions)));
            services.Configure<DIOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(DIOptions)}"));
            services.Configure<VivCacheOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(VivCacheOptions)}"));
            services.Configure<RedisOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(VivCacheOptions)}:{nameof(RedisOptions)}"));
            services.Configure<LogOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(LogOptions)}"));
            services.Configure<DatabaseOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(DatabaseOptions)}"));
            services.Configure<NanaOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(NanaOptions)}"));
            services.Configure<TokenOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(TokenOptions)}"));
            services.Configure<TickOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(TickOptions)}"));
            services.Configure<TickerQOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(TickOptions)}:{nameof(TickerQOptions)}"));
            services.Configure<EchoOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(EchoOptions)}"));
            services.Configure<GrpcOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(EchoOptions)}:{nameof(GrpcOptions)}"));
            services.Configure<CorsOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(CorsOptions)}"));
            services.Configure<OpenAIOptions>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(OpenAIOptions)}"));
            services.Configure<S3Options>(configuration.GetSection($"{nameof(VivOptions)}:{nameof(S3Options)}"));

            return services;
        }

        /// <summary>
        /// 将非 null 的配置实例注册到 DI 容器（T + IOptions&lt;T&gt;）
        /// </summary>
        private static void RegisterOptions(IServiceCollection services, VivOptions options)
        {
            // 主配置
            if (options != null)
                RegisterOption(services, options);

            if (options == null)
                return;

            // EnvOption
            if (options.EnvOption != null)
            {
                RegisterOption(services, options.EnvOption);
                // VivInternalTokenOptions 由 EnvOption 派生，如果 EnvOption 不为 null 才构造
                var internalTokenOptions = new VivInternalTokenOptions
                {
                    InternalToken = options.EnvOption.InternalToken,
                    ServiceName = options.EnvOption.ServiceName
                };
                RegisterOption(services, internalTokenOptions);
            }

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
            services.AddSingleton(Options.Create(value));
        }
    }
}