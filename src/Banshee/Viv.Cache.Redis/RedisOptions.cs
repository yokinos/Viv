using System;
using System.Collections.Generic;
using System.Text;
using Viv.Vva.Extension;

namespace Viv.Cache.Redis
{
    /// <summary>
    /// Redis配置模型（适配StackExchange.Redis 2.7+）
    /// </summary>
    public class RedisOptions
    {
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
        /// 判断是否为哨兵模式
        /// </summary>
        public bool IsSentinelMode { get; set; } = false;

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
    }
}
