using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Viv.Aoi;
using Viv.Clockwork.Options;
using Viv.Contracts.Options;
using Viv.Delusion;
using Viv.Keke;
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

            // 模块配置段：路径约定 VivOptions:{Section}，清单来自 VivModules.All。
            // 手抄清单漏过 MqttOptions / KinnaOptions / SkirkOption —— 那三个节点会让注入解析失败
            foreach (var module in VivModules.All)
            {
                if (module.OptionsType is not { } optionsType || module.Section is not { } section) continue;
                ConfigureSection(services, optionsType, configuration.GetSection($"VivOptions:{section}"));
            }

            // 宿主级配置：不是可插拔模块，没有开关
            services.Configure<VivOptions>(configuration.GetSection("VivOptions"));
            services.Configure<EnvOptions>(configuration.GetSection("VivOptions:EnvOption"));
            services.Configure<DIOptions>(configuration.GetSection("VivOptions:DIOption"));
            services.Configure<CorsOptions>(configuration.GetSection("VivOptions:CorsOption"));

            // 嵌套子配置：路径比模块名深一层，描述符里推不出来，只有这三处
            services.Configure<RedisOptions>(configuration.GetSection("VivOptions:CacheOption:RedisOptions"));
            services.Configure<TickerQOptions>(configuration.GetSection("VivOptions:TickOption:TickerQ"));
            services.Configure<GrpcOptions>(configuration.GetSection("VivOptions:KekeOption:GrpcOption"));

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
        /// 将非 null 的配置实例注册到 DI 容器（T + IOptions&lt;T&gt;）。
        ///
        /// 模块配置段的清单来自 <see cref="VivModules.All"/>，这里不含散落的 if。
        /// </summary>
        private static void RegisterOptions(IServiceCollection services, VivOptions options)
        {
            // 主配置
            RegisterOption(services, options);

            // 宿主级配置：不是可插拔模块，没有开关
            if (options.EnvOption != null) RegisterOption(services, options.EnvOption);
            if (options.DIOption != null) RegisterOption(services, options.DIOption);
            if (options.CorsOption != null) RegisterOption(services, options.CorsOption);

            // VivInternalTokenOptions 由 EnvOption 派生，恒注册（EnvOption 缺失时给空对象）：
            // Keke gRPC 拦截器构造注入它，未注册会直接导致 gRPC 端激活失败
            RegisterOption(services, new VivInternalTokenOptions
            {
                InternalToken = options.EnvOption?.InternalToken,
                ServiceName = options.EnvOption?.ServiceName
            });

            // 模块配置段（清单唯一来源）。这里刻意不看 IsEnabled —— 配了就该能注入，
            // 「装了没装」是 VivModules.RegisterAll 的事
            foreach (var module in VivModules.All)
            {
                if (module.OptionsOf(options) is { } moduleOptions) RegisterOptionInstance(services, moduleOptions);
            }

            // 嵌套子配置：路径比模块名深一层，描述符里推不出来，只有这三处
            if (options.CacheOption?.RedisOptions is { } redisOptions) RegisterOption(services, redisOptions);
            if (options.TickOption?.TickerQ is { } tickerQOptions) RegisterOption(services, tickerQOptions);
            if (options.KekeOption?.GrpcOption is { } grpcOptions) RegisterOption(services, grpcOptions);
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

        /// <summary>
        /// 非泛型重载：遍历描述符时手里只有 <c>object</c>，泛型参数 T 要到运行期才知道。
        ///
        /// 不能直接调 <see cref="RegisterOption{T}"/> —— 传 <c>object</c> 会被推断成 T=object，
        /// 于是登记出 <c>IOptions&lt;object&gt;</c>，注入 <c>IOptions&lt;DatabaseOptions&gt;</c> 时解析不到。
        /// 所以这里按运行期类型登记，<c>IOptions&lt;T&gt;</c> 用反射构造一次 <c>Options.Create&lt;T&gt;</c>：
        /// 启动期跑十几次、不在热路径上，代价可以忽略。
        /// </summary>
        private static void RegisterOptionInstance(IServiceCollection services, object value)
        {
            var type = value.GetType();
            services.AddSingleton(type, value);

            var createOptions = CreateOptionsMethod.MakeGenericMethod(type);
            services.AddSingleton(typeof(IOptions<>).MakeGenericType(type), createOptions.Invoke(null, [value])!);
        }

        /// <summary>
        /// 把一段配置登记成 <c>IOptions&lt;T&gt;</c>（保留 IOptionsMonitor 热更新语义）。
        ///
        /// 没有 <c>Configure(Type, IConfiguration)</c> 这样的非泛型重载 —— 只有泛型版本，
        /// 而 T 要到运行期才知道，只能反射调一次。启动期十几次，不在热路径上。
        /// </summary>
        private static void ConfigureSection(IServiceCollection services, Type optionsType, IConfiguration section)
        {
            ConfigureSectionMethod.MakeGenericMethod(optionsType).Invoke(null, [services, section]);
        }

        private static readonly MethodInfo ConfigureSectionMethod =
            typeof(OptionsConfigurationServiceCollectionExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(OptionsConfigurationServiceCollectionExtensions.Configure)
                             && m.IsGenericMethodDefinition
                             && m.GetParameters() is { Length: 2 } parameters
                             && parameters[1].ParameterType == typeof(IConfiguration));

        private static readonly MethodInfo CreateOptionsMethod =
            typeof(Microsoft.Extensions.Options.Options).GetMethod(
                nameof(Microsoft.Extensions.Options.Options.Create),
                BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("找不到 Options.Create<T>，无法注册 IOptions<T>。");
    }
}