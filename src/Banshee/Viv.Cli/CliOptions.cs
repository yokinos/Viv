using Spectre.Console;

namespace Viv.Cli
{
    public class CliOptions
    {
        /// <summary>
        /// Figlet 横幅标题（默认 "Viv CLI"）
        /// </summary>
        public string BannerTitle { get; set; } = "Viv CLI";

        /// <summary>
        /// 应用名称（用于 --help，默认 "viv"）
        /// </summary>
        public string AppName { get; set; } = "viv";

        /// <summary>
        /// 提示符（默认 "> "）
        /// </summary>
        public string Prompt { get; set; } = ">";

        /// <summary>
        /// 横幅颜色（默认蓝色）
        /// </summary>
        public Color BannerColor { get; set; } = Color.Blue;

        /// <summary>
        /// 提示行文本（null 则使用默认）
        /// </summary>
        public string? HintText { get; set; }

        /// <summary>
        /// 控制台窗口标题（null 则使用 BannerTitle）
        /// </summary>
        public string? WindowTitle { get; set; }
    }
}
