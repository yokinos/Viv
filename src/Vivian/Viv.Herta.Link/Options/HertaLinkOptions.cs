namespace Viv.Herta.Link.Options
{
    public class HertaLinkOptions
    {
        public string HubPath { get; set; } = "/chat";

        public bool EnableDetailedErrors { get; set; }

        /// <summary>连接租约时长。超过它没收到 Ping 的连接会被清扫掉</summary>
        public int EntryTtlSeconds { get; set; } = 90;

        /// <summary>租约清扫间隔</summary>
        public int SweepIntervalSeconds { get; set; } = 60;
    }
}
