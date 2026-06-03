using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Aspire.ServiceDefaults
{
    public class AspireParameter
    {
        public RabbitMQConfig RabbitMQConfig { get; set; }
        public RedisConfig RedisConfig { get; set; }
        public DistributedLogConfig DistributedLogConfig { get; set; }
        public Dictionary<string, DatabaseConfig> DatabaseConfigs { get; set; }
    }

    public record RabbitMQConfig(string Host, string Username, string Password, string VirtualHost, int Port);
    public record RedisConfig(string[] Host, int Port, string Password, int RedisMode);
    public record DatabaseConfig(int DatabaseSouceType, bool IsReadWriteSplit, string MasterConnectionString, string[] SlaveConnectionString, string SagaConnectionString, string TickerQConnectionString);
    public record DistributedLogConfig(bool IsEnabled, string SeqUrl, string SeqApiKey);
}
