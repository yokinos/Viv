using Viv.Momo.Enums;

namespace Viv.Sayu
{
    public class TickerQOptions
    {
        /// <summary>
        /// 最大并发执行数
        /// </summary>
        public int MaxConcurrency { get; set; } = 4;

        /// <summary>
        /// TickerQ 操作用数据库连接字符串（不配则使用内存存储，重启后任务丢失）
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// TickerQ 操作用数据库类型
        /// </summary>
        public DatabaseSouceType DatabaseType { get; set; } = DatabaseSouceType.PostgreSQL;

        public bool EnableDashboard { get; set; } = true;

        public string DashboardPath { get; set; } = "/tickerq";
    }
}
