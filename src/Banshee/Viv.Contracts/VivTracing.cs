using System.Diagnostics;

namespace Viv.Contracts
{
    /// <summary>
    /// 框架自己发的 span。源名定义在这儿，Viv.Aspire.ServiceDefaults 按它 AddSource。
    ///
    /// 与 VivHeaderContract 同一个考虑：跨层共用的字符串只留一个来源。放项目根，
    /// 与 LockKeyMagic 并排 —— 它俩都是框架层要共用的静态小工具。
    /// </summary>
    public static class VivTracing
    {
        public const string SourceName = "Viv";

        public static readonly ActivitySource Source = new(SourceName);
    }
}
