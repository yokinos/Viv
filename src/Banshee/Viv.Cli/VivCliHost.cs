using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;

namespace Viv.Cli
{
    public class VivCliHost
    {
        private readonly CommandApp _app;
        private readonly CliOptions _options;
        private readonly CliTypeRegistrar? _registrar;
        private readonly List<Type> _commandTypes = [];
        private static VivCliHost? _current;

        public static VivCliHost Current => _current!;

        /// <summary>
        /// services 传宿主容器的注册表，命令类型登记进去，命令才注入得到 IMomoDbContext
        /// 这类服务；不传就退回 Spectre 自己的无参构造，命令只能没有依赖。
        ///
        /// 传了 services 就要在 AddVivApp 之前构造，容器是那一刻建起来的。
        /// </summary>
        public VivCliHost(CliOptions? options = null, IServiceCollection? services = null)
        {
            _options = options ?? new CliOptions();

            if (services != null)
            {
                _registrar = new CliTypeRegistrar();
                _app = new CommandApp(_registrar);
            }
            else
            {
                _app = new CommandApp();
            }

            _app.Configure(config =>
            {
                config.SetApplicationName(_options.AppName);
                // 内置命令（Viv.Cli 程序集）
                _commandTypes.AddRange(config.ScanCommands(typeof(VivCliHost).Assembly, services));
                // 项目命令（入口程序集）
                var entry = Assembly.GetEntryAssembly();
                if (entry != null && entry != typeof(VivCliHost).Assembly)
                    _commandTypes.AddRange(config.ScanCommands(entry, services));
            });
            _current = this;
        }

        public CliOptions Options => _options;

        /// <summary>
        /// 接上宿主容器。StartAsync 之后调一次，命令才解析得出来。
        /// </summary>
        public void UseContainer(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            if (_registrar == null)
            {
                throw new InvalidOperationException("构造 VivCliHost 时没有传 services，命令不走 DI，不需要接容器。");
            }

            // 登记过不等于进得了容器。IServiceProviderIsService 只查登记表、不实例化，
            // provider 给不了这个能力（非 MS DI）就跳过。
            if (provider.GetService<IServiceProviderIsService>() is { } isService)
            {
                var missing = _commandTypes.FirstOrDefault(t => !isService.IsService(t));
                if (missing != null)
                {
                    throw new InvalidOperationException(
                        $"命令 {missing.Name} 不在宿主容器里 —— VivCliHost 要在 AddVivApp 之前构造。");
                }
            }

            _registrar.Resolver.Provider = provider;
        }

        public async Task RunAsync()
        {
            Console.Title = _options.WindowTitle ?? _options.BannerTitle;

            PrintBanner();

            while (true)
            {
                var prompt = new TextPrompt<string>($"[blue]{_options.Prompt}[/]")
                    .PromptStyle("blue")
                    .AllowEmpty();

                var input = AnsiConsole.Prompt(prompt);

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                var trimmed = input.Trim();

                if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (trimmed is "help" or "?" or "h")
                    trimmed = "--help";

                // 一条命令一个作用域，命令拿到的 Scoped 服务随之在命令结束后一起收掉
                _registrar?.Resolver.BeginScope();

                try
                {
                    await _app.RunAsync(trimmed.Split(' '));
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                }
                finally
                {
                    _registrar?.Resolver.EndScope();
                }

                AnsiConsole.WriteLine();
            }
        }

        public void PrintBanner()
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText(_options.BannerTitle).Color(_options.BannerColor));
            var hint = _options.HintText ?? "输入命令执行 | --help 查看帮助 | exit 退出";
            AnsiConsole.MarkupLine($"[grey]{hint}[/]");
            AnsiConsole.WriteLine();
        }
    }

    internal static class VivCliConfiguratorExtensions
    {
        /// <summary>
        /// 扫程序集里的 [VivCommand] 登记成命令，返回登记到的类型。
        ///
        /// services 非空时把命令类型一并写进宿主容器的注册表，命令注入得到依赖全靠它。
        /// </summary>
        public static IReadOnlyList<Type> ScanCommands(this IConfigurator config, Assembly assembly, IServiceCollection? services = null)
        {
            var commandTypes = assembly
                .GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }&& t.GetCustomAttribute<VivCommandAttribute>() != null)
                .ToList();

            foreach (var type in commandTypes)
            {
                var attr = type.GetCustomAttribute<VivCommandAttribute>()!;

                try
                {
                    services?.AddTransient(type);

                    var addCmdMethod = typeof(IConfigurator)
                        .GetMethods()
                        .First(m => m.Name == nameof(IConfigurator.AddCommand)&& m.GetParameters().Length == 1&& m.GetGenericArguments().Length == 1)
                        .MakeGenericMethod(type);

                    var cmdConfig = addCmdMethod.Invoke(config, [attr.PrimaryName])!;
                    var cmdConfigType = cmdConfig.GetType();

                    // 描述（含别名提示）
                    cmdConfigType.GetMethod("WithDescription")?.Invoke(cmdConfig, [attr.FullDescription]);

                    // 别名运行时生效
                    if (attr.Names.Length > 1)
                    {
                        var withAlias = cmdConfigType.GetMethod("WithAlias");
                        if (withAlias != null)
                        {
                            foreach (var alias in attr.Names.Skip(1))
                                withAlias.Invoke(cmdConfig, [alias]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]警告: 无法注册命令 [bold]{attr.Name}[/] ({type.Name}): {Markup.Escape(ex.Message)}[/]");
                }
            }

            return commandTypes;
        }
    }
}
