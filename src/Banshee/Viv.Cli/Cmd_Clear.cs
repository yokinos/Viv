using Spectre.Console;
using Spectre.Console.Cli;

namespace Viv.Cli
{
    [VivCommand("clear, cl", "清除屏幕")]
    public class Cmd_Clear : AsyncCommand
    {
        protected override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            AnsiConsole.Clear();
            VivCliHost.Current.PrintBanner();
            return Task.FromResult(0);
        }
    }
}
