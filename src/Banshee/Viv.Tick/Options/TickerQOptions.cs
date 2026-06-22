using Viv.Momo.Enums;

namespace Viv.Tick.Options
{
    public class TickerQOptions
    {
        /// <summary>
        /// TickerQ 操作用数据库连接字符串（不配则使用内存存储，重启后任务丢失）
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// TickerQ 操作用数据库类型
        /// </summary>
        public DatabaseSourceType DatabaseType { get; set; } = DatabaseSourceType.SqlServer;

        public string EFCoreSchemaName { get; set; } = "dbo";

        public string? AssemblyName { get; set; }

        /// <summary>
        /// 是否启用 TickerQ 仪表盘
        /// </summary>
        public bool EnableDashboard { get; set; } = true;

        /// <summary>
        /// TickerQ 仪表盘配置
        /// </summary>
        public TickerQDashboradOptions DashboardOptions { get; set; } = new TickerQDashboradOptions();
    }

    public class TickerQDashboradOptions
    {
        /// <summary>
        /// TickerQ 仪表盘路径
        /// </summary>
        public string DashboardPath { get; set; } = "/tickerq";

        /// <summary>
        /// 账号
        /// </summary>
        public string UserName { get; set; } = "viv";

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; } = "viv_tickerq_77";

        /// <summary>
        /// 使用ApiKey进行认证
        /// </summary>
        public string WebApiKey { get; set; } = string.Empty;

    }
}
