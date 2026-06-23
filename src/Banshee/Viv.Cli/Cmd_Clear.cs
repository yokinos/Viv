using Spectre.Console;
using Spectre.Console.Cli;

namespace Viv.Cli
{
    [VivCommand("clear, cl", "清除屏幕")]
    public class Cmd_Clear : AsyncCommand
    {
        public override Task<int> ExecuteAsync(CommandContext context)
        {
            AnsiConsole.Clear();
            VivCliHost.Current.PrintBanner();
            return Task.FromResult(0);
        }
    }
}
