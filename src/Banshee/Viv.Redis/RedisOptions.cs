using System;
using System.Collections.Generic;
using System.Text;
using Viv.Redis.DbAllocator;
using Viv.Vva.Extension;

namespace Viv.Redis
{
    /// <summary>
    /// Redis配置模型（适配StackExchange.Redis 2.7+）
    /// </summary>
    public class RedisOptions
    {
        /// <summary>
        /// Redis部署模式
        /// </summary>
        public RedisMode RedisMode { get; set; } = RedisMode.Standalone;

        /// <summary>
        /// Redis 连接字符串
        /// 单体示例: "127.0.0.1:6379,password=123456"
        /// 集群示例: "127.0.0.1:6379,127.0.0.1:6380,127.0.0.1:6381,password=123456,allowAdmin=true"
        /// 哨兵无需填写此值，通过 SentinelEndPoints 配置
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// 哨兵模式 - 哨兵节点列表（格式：ip:port）
        /// 示例: new List<string> { "127.0.0.1:26379", "127.0.0.1:26380" }
        /// </summary>
        public List<string> SentinelEndPoints { get; set; } = [];

        /// <summary>
        /// 哨兵模式 - 主节点名称（必填，如 "mymaster"）
        /// </summary>
        public string SentinelMasterName { get; set; } = string.Empty;

        /// <summary>
        /// 连接超时时间（毫秒），默认 5000
        /// </summary>
        public int ConnectTimeout { get; set; } = 5000;

        /// <summary>
        /// 同步操作超时时间（毫秒），默认 5000
        /// </summary>
        public int SyncTimeout { get; set; } = 5000;

        /// <summary>
        /// 是否允许管理员操作（集群/哨兵模式下建议开启）
        /// </summary>
        public bool AllowAdmin { get; set; } = false;

        /// <summary>
        /// 连接失败时是否重试
        /// </summary>
        public bool AbortOnConnectFail { get; set; } = false;

        /// <summary>
        /// Redis 密码（哨兵模式下统一配置在这里）
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 默认数据库[0-15]（哨兵/单体模式有效）
        /// </summary>
        public int DefaultDatabase { get; set; } = 0;

        /// <summary>
        /// 有效性保持时间（秒），默认 60 秒
        /// </summary>
        public int KeepAlive { get; set; } = 60;

        /// <summary>
        /// Redis数据库最大可用索引（决定应用可使用的DB范围）
        /// 注意：在正式使用后这个值就不允许修改了,修改会导致哈希错误 最终key定位错误
        /// 【关键说明】
        /// 1. 单体Redis（单实例/主从）：有效，应用仅能使用 0 ~ MaxDbIndex 的DB（包含边界）；
        ///    - 示例：MaxDbIndex=0 → 仅能用DB 0；MaxDbIndex=1 → 能用DB 0、1；
        /// 2. Redis集群（Cluster）：强制为0且不可修改，因集群模式不支持多DB（所有操作默认DB 0）；
        /// 3. 哨兵模式（Sentinel）：本质是单体Redis的高可用方案，支持多DB，该配置有效；
        /// </summary>
        /// <remarks>
        /// 注意：
        /// - Redis默认内置16个DB（索引0-15），该值建议不超过13（预留2个DB给运维/测试）；
        /// - 生产环境不推荐使用多DB，建议通过Key前缀（如user:xxx、order:xxx）隔离，或部署多实例；
        /// </remarks>
        public int MaxDbIndex { get; set; } = 12;

        /// <summary>
        /// 你可以自定义Redis库的分库方式,默认按照key分库
        /// </summary>
        public DbSelectorType SelectorType { get; set; } = DbSelectorType.None;
    }
}
