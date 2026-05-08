using Viv.Momo.Enums;
using Viv.Vva.Magic;

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
        /// Saga 状态机扫描规则（非空时启用 Saga）
        /// </summary>
        public List<FilterTypeOptions> SagaStateMachineTypes { get; set; } = [];

        /// <summary>
        /// Saga 持久化数据库类型（PostgreSQL / SqlServer）
        /// </summary>
        public DatabaseSouceType SagaDatabaseSouce { get; set; } = DatabaseSouceType.PostgreSQL;

        /// <summary>
        /// Saga 持久化数据库连接字符串（默认复用 Momo 主库）
        /// </summary>
        public string? SagaConnectionString { get; set; }
    }
}
