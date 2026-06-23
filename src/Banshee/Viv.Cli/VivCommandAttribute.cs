namespace Viv.Cli
{
    /// <summary>
    /// 标记一个类为 Viv CLI 命令，自动扫描注册到 CommandApp。
    /// 支持逗号分隔多个命令名，如 "clear, cl"。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class VivCommandAttribute : Attribute
    {
        /// <summary>
        /// 命令名（逗号分隔多个别名，第一个为主命令名）
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 命令描述
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 所有命令名（含别名）
        /// </summary>
        public string[] Names => Name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// 主命令名
        /// </summary>
        public string PrimaryName => Names[0];

        /// <summary>
        /// 带别名提示的描述，如 "清除屏幕（别名: cl）"
        /// </summary>
        public string FullDescription =>
            Names.Length > 1
                ? $"{Description}（别名: {string.Join(", ", Names.Skip(1))}）"
                : Description;

        public VivCommandAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
