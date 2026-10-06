using Spectre.Console;

namespace Viv.Cli
{
    /// <summary>
    /// 交互式输入工具。提示文本一律按纯文本处理（内部已做 markup 转义），
    /// 调用方可以放心在提示里写 [默认值] 这类方括号内容。
    /// </summary>
    public static class InputMagic
    {
        /// <summary>
        /// 获取用户输入
        /// </summary>
        /// <param name="prompt">提示文本（纯文本，方括号无需自行转义）</param>
        /// <param name="allowEmpty">是否允许空输入（默认 false）</param>
        /// <param name="secret">是否隐藏输入（密码等）</param>
        public static string GetInput(string prompt, bool allowEmpty = false, bool secret = false)
        {
            var textPrompt = new TextPrompt<string>($"[blue]{Markup.Escape(prompt)}[/]").PromptStyle("blue");

            if (allowEmpty) textPrompt.AllowEmpty();
            if (secret) textPrompt.Secret();

            return AnsiConsole.Prompt(textPrompt);
        }

        /// <summary>
        /// 获取用户确认（y/n）
        /// </summary>
        /// <param name="prompt">提示文本（纯文本，方括号无需自行转义）</param>
        public static bool Confirm(string prompt)
        {
            return AnsiConsole.Confirm($"[blue]{Markup.Escape(prompt)}[/]");
        }

        /// <summary>
        /// 获取用户选择。标题按纯文本处理，选项文本本身就是纯文本，不受 markup 影响
        /// </summary>
        /// <param name="prompt">标题（纯文本，方括号无需自行转义）</param>
        /// <param name="choices">候选项</param>
        public static string Select(string prompt, params string[] choices)
        {
            return AnsiConsole.Prompt(new SelectionPrompt<string>().Title($"[blue]{Markup.Escape(prompt)}[/]").AddChoices(choices));
        }
    }
}
