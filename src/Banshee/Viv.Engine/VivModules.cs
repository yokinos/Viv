using Microsoft.Extensions.DependencyInjection;
using Viv.Aoi;
using Viv.Clockwork.Options;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Engine.LocalEvents;
using Viv.Engine.Options;
using Viv.Keke;
using Viv.Kinna.Options;
using Viv.Log;
using Viv.Momo.Options;
using Viv.Nana.Options;
using Viv.Outbox.Options;
using Viv.Sandrone.Impl;
using Viv.Skirk.Options;

namespace Viv.Engine
{
    /// <summary>
    /// 一个可插拔子系统的描述符：叫什么、配置在哪段、什么条件下装、怎么装。
    ///
    /// 加模块只改 <see cref="VivModules.All"/> 一处 —— 配置对象注册与动态 Configure 都从这张表遍历，
    /// 不再依赖各自手抄的 if 列表（那种写法漏过：静态路径漏 <c>SkirkOption</c>，
    /// 动态路径漏 <c>MqttOptions</c>/<c>KinnaOptions</c>/<c>SkirkOption</c>）。
    /// </summary>
    /// <param name="Name">模块名，只用于日志与摘要</param>
    /// <param name="OptionsType">配置类型；<c>null</c> = 该模块没有独立配置段</param>
    /// <param name="Section">配置段名（<c>VivOptions.{Section}</c>）。<b>与 <paramref name="Name"/> 分开</b> ——
    /// 模块叫 Scheduler 但配置段是 <c>TickOption</c>，模块叫 Database 但段是 <c>DatabaseOption</c>，
    /// 按名字拼路径会拼错。有 <paramref name="OptionsType"/> 就必须有它</param>
    /// <param name="OptionsOf">取配置实例，供「配置对象注册进 DI」遍历。与 <paramref name="IsOn"/> 分开，
    /// 是因为「配了就能注入」和「装了没装」是两件事：<c>IsEnabled = false</c> 时业务仍该能注入配置读到值</param>
    /// <param name="IsOn">装不装。统一语义是「节点在」且「没被显式关掉」</param>
    /// <param name="Register">装配动作</param>
    internal sealed record VivModule(
        string Name,
        Type? OptionsType,
        string? Section,
        Func<VivOptions, object?> OptionsOf,
        Func<VivOptions, bool> IsOn,
        Action<IServiceCollection, VivOptions> Register);

    /// <summary>
    /// 全量模块清单。<b>顺序即装配顺序，不要按字母排</b> —— Nana 的 Saga DbContext 必须先于
    /// <c>UseWolverine</c>（Wolverine 文档的顺序），而 <see cref="VivRegister.RegisterNana"/> 内部
    /// 依赖这一点；日志也要排在最前面，后面几个模块出错时的报错才知道往哪写。
    /// </summary>
    internal static class VivModules
    {
        public static readonly IReadOnlyList<VivModule> All =
        [
            new("Log", typeof(LogOptions), "LogOption",o => o.LogOption, o => o.LogOption is { IsEnabled: true },VivRegister.RegisterLogger),
            new("Keke", typeof(KekeOptions), "KekeOption",o => o.KekeOption, o => o.KekeOption is { IsEnabled: true },VivRegister.RegisterKeke),
            new("Cache", typeof(VivCacheOptions), "CacheOption",o => o.CacheOption, o => o.CacheOption is { IsEnabled: true },VivRegister.RegisterCache),
            new("Nana", typeof(NanaOptions), "NanaOption",o => o.NanaOption, o => o.NanaOption is { IsEnabled: true },VivRegister.RegisterNana),
            new("Mqtt", typeof(MqttOptions), "MqttOption",o => o.MqttOption, o => o.MqttOption is { IsEnabled: true },VivRegister.RegisterMqtt),
            new("Database", typeof(DatabaseOptions), "DatabaseOption",o => o.DatabaseOption, o => o.DatabaseOption is { IsEnabled: true },VivRegister.RegisterDatabase),
            new("HealthChecks", null, null, _ => null, _ => true,VivRegister.RegisterHealthChecks),
            new("Outbox", typeof(OutboxOptions), "OutboxOption",o => o.OutboxOption, o => o.OutboxOption is { IsEnabled: true },VivRegister.RegisterOutbox),
            new("Inbox", typeof(InboxOptions), "InboxOption",o => o.InboxOption,o => o.DatabaseOption is { IsEnabled: true } && (o.InboxOption is null || o.InboxOption.IsEnabled),VivRegister.RegisterInbox),
            new("Skirk", typeof(SkirkOption), "SkirkOption",o => o.SkirkOption, o => o.SkirkOption is { IsEnabled: true },VivRegister.RegisterSkirk),
            new("Kinna", typeof(KinnaOptions), "KinnaOption",o => o.KinnaOption, o => o.KinnaOption is { IsEnabled: true },VivRegister.RegisterKinna),
            new("Token", typeof(TokenOptions), "TokenOption",o => o.TokenOption, _ => true,VivRegister.RegisterToken),
            new("Scheduler", typeof(TickOptions), "TickOption",o => o.TickOption, o => o.TickOption is { IsEnabled: true },VivRegister.RegisterScheduler),
            new("S3", typeof(S3Options), "S3Option",o => o.S3Option, o => o.S3Option is { IsEnabled: true },VivRegister.RegisterS3),
            new("LocalEvent", null, null, _ => null, _ => true,static (services, _) => LocalEventRegistration.Register(services)),
            new("OtherServices", null, null, _ => null, _ => true,VivRegister.RegisterOtherServices),
        ];

        /// <summary>
        /// 装配全部启用模块，并打一行摘要。
        ///
        /// 摘要分「启用 / 跳过」两段，跳过项带原因（<c>未配置</c> / <c>IsEnabled=false</c>）——
        /// 「配了但没生效」是框架侧第一类排障问题，而这两种原因的处置方式完全不同，不能合成一个词。
        /// </summary>
        public static void RegisterAll(IServiceCollection services, VivOptions options)
        {
            // 常驻项：没有配置开关，必须在模块循环之前 —— 指标 listener 要尽早挂上，
            // 它只收「起来之后」的测量，晚一步就丢一段
            services.AddSingleton<IVivMeter>(new Metrics.VivMeter());
            services.AddSingleton<IVivContextProvider, DefaultVivContextProvider>();
            services.AddSingleton<IVivContextAccessor, VivContextAccessor>();
            services.AddScoped<IVivContext, VivContext>();

            var enabled = new List<string>(All.Count);
            var skipped = new List<string>();

            foreach (var module in All)
            {
                if (module.IsOn(options))
                {
                    module.Register(services, options);
                    enabled.Add(Describe(module, options));
                }
                else
                {
                    // IsOn 为 false 且配置实例存在 ⇒ 是 IsEnabled=false 关掉的；
                    // 实例为 null ⇒ 节点没配。这两个原因要分开，排障时处置完全不同。
                    var reason = module.OptionsOf(options) is null ? "未配置" : "IsEnabled=false";
                    skipped.Add($"{module.Name}({reason})");
                }
            }

            LogSummary(enabled, skipped);
        }

        /// <summary>
        /// 摘要里的模块名。Token 额外标出实际装的是 Jwt 还是 None —— 配了节点但 SecretKey 为空
        /// 时 CanRegister 返回 false，实际回落 NoneTokenService，只看「已启用」看不出这层。
        /// </summary>
        private static string Describe(VivModule module, VivOptions options)
        {
            if (module.Name != "Token") return module.Name;

            return VivJwtBearerHelper.CanRegister(options.TokenOption) ? "Token(jwt)" : "Token(none)";
        }

        private static void LogSummary(List<string> enabled, List<string> skipped)
        {
            var on = enabled.Count == 0 ? "无" : string.Join(", ", enabled);
            var off = skipped.Count == 0 ? "无" : string.Join(", ", skipped);

            var message = $"Viv 模块装配：启用 {enabled.Count} [{on}]；跳过 {skipped.Count} [{off}]";

            // 日志是可选依赖：没配 LogOption 的宿主连 Serilog 都没初始化，这时退回控制台 ——
            // 摘要本身就是要给人看的，静默丢掉等于没打
            if (SerilogProvider.IsInitialized)
            {
                SerilogProvider.GetLogger().Information(message);
                return;
            }

            Console.WriteLine(message);
        }
    }
}
