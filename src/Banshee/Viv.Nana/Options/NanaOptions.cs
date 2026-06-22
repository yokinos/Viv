using Viv.Delusion.Magic;
using Viv.Momo.Enums;

namespace Viv.Nana.Options
{
    public class NanaOptions
    {
        /// <summary>
        /// RabbitMQ 主机地址
        /// </summary>
        public string Host { get; set; } = "localhost";

        /// <summary>
        /// RabbitMQ 端口
        /// </summary>
        public int Port { get; set; } = 5672;

        /// <summary>
        /// RabbitMQ 用户名
        /// </summary>
        public string UserName { get; set; } = "guest";

        /// <summary>
        /// RabbitMQ 密码
        /// </summary>
        public string Password { get; set; } = "guest";

        /// <summary>
        /// RabbitMQ 虚拟主机
        /// </summary>
        public string VirtualHost { get; set; } = "/";

        /// <summary>
        /// 消费失败后的重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 要注册的消费者类型
        /// </summary>
        public List<FilterTypeOptions> ConsumerTypes { get; set; } = [];

        /// <summary>
        /// Saga 持久化数据库类型（PostgreSQL / SqlServer）
        /// </summary>
        public DatabaseSourceType SagaDatabaseSouce { get; set; } = DatabaseSourceType.PostgreSQL;

        /// <summary>
        /// Saga 持久化数据库连接字符串（不配则不启用 Saga）
        /// </summary>
        public string? SagaConnectionString { get; set; }
    }
}
