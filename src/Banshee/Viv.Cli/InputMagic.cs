using Spectre.Console;

namespace Viv.Cli
{
    /// <summary>
    /// 交互式输入工具
    /// </summary>
    public static class InputMagic
    {
        /// <summary>
        /// 获取用户输入
        /// </summary>
        /// <param name="prompt">提示文本</param>
        /// <param name="allowEmpty">是否允许空输入（默认 false）</param>
        /// <param name="secret">是否隐藏输入（密码等）</param>
        public static string GetInput(string prompt, bool allowEmpty = false, bool secret = false)
        {
            var textPrompt = new TextPrompt<string>($"[blue]{prompt}[/]")
                .PromptStyle("blue");

            if (allowEmpty) textPrompt.AllowEmpty();
            if (secret) textPrompt.Secret();

            return AnsiConsole.Prompt(textPrompt);
        }

        /// <summary>
        /// 获取用户确认（y/n）
        /// </summary>
        public static bool Confirm(string prompt)
        {
            return AnsiConsole.Confirm($"[blue]{prompt}[/]");
        }

        /// <summary>
        /// 获取用户选择
        /// </summary>
        public static string Select(string prompt, params string[] choices)
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[blue]{prompt}[/]")
                    .AddChoices(choices));
        }
    }
}
