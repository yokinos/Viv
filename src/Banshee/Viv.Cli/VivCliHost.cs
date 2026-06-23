using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;

namespace Viv.Cli
{
    public class VivCliHost
    {
        private readonly CommandApp _app;
        private readonly CliOptions _options;
        private static VivCliHost? _current;

        public static VivCliHost Current => _current!;

        public VivCliHost(CliOptions? options = null)
        {
            _options = options ?? new CliOptions();
            _app = new CommandApp();
            _app.Configure(config =>
            {
                config.SetApplicationName(_options.AppName);
                // 内置命令（Viv.Cli 程序集）
                config.ScanCommands(typeof(VivCliHost).Assembly);
                // 项目命令（入口程序集）
                var entry = Assembly.GetEntryAssembly();
                if (entry != null && entry != typeof(VivCliHost).Assembly)
                    config.ScanCommands(entry);
            });
            _current = this;
        }

        public CliOptions Options => _options;

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

                try
                {
                    await _app.RunAsync(trimmed.Split(' '));
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                }

                AnsiConsole.WriteLine();
            }
        }

        public void PrintBanner()
        {
            AnsiConsole.Write(new FigletText(_options.BannerTitle).Color(_options.BannerColor));
            var hint = _options.HintText ?? "输入命令执行 | --help 查看帮助 | exit 退出";
            AnsiConsole.MarkupLine($"[grey]{hint}[/]");
            AnsiConsole.WriteLine();
        }
    }

    internal static class VivCliConfiguratorExtensions
    {
        public static void ScanCommands(this IConfigurator config, Assembly assembly)
        {
            var commandTypes = assembly
                .GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                            && t.GetCustomAttribute<VivCommandAttribute>() != null);

            foreach (var type in commandTypes)
            {
                var attr = type.GetCustomAttribute<VivCommandAttribute>()!;

                try
                {
                    var addCmdMethod = typeof(IConfigurator)
                        .GetMethods()
                        .First(m => m.Name == nameof(IConfigurator.AddCommand)
                                    && m.GetParameters().Length == 1
                                    && m.GetGenericArguments().Length == 1)
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
        }
    }
}
