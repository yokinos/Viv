using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    public class AspireParameter
    {
        public RabbitMQConfig RabbitMQConfig { get; set; }
        public RedisConfig RedisConfig { get; set; }
        public DistributedLogConfig DistributedLogConfig { get; set; }
        public Dictionary<string, BusinessDatabaseConfig> DatabaseConfigs { get; set; }
        public MiddlewareDatabaseConfig MiddlewareDatabaseConfig { get; set; }
        public SeqConfig SeqConfig { get; set; }

        public string GetRedisConnectionString()
        {
            if (RedisConfig == null || !RedisConfig.Host.Any())
                return string.Empty;

            var hosts = string.Join(",", RedisConfig.Host.Select(h => $"{h}:{RedisConfig.Port}"));
            var auth = string.IsNullOrWhiteSpace(RedisConfig.Password)
                ? string.Empty
                : $":{RedisConfig.Password}@";

            return $"redis://{auth}{hosts}";
        }

        public string GetRabbitMQConnectionString()
        {
            if (RabbitMQConfig == null) return string.Empty;

            var vHost = string.IsNullOrWhiteSpace(RabbitMQConfig.VirtualHost)
                ? "/"
                : RabbitMQConfig.VirtualHost;

            return $"amqp://{RabbitMQConfig.Username}:{RabbitMQConfig.Password}@{RabbitMQConfig.Host}:{RabbitMQConfig.Port}/{vHost}";
        }
    }

    public record RabbitMQConfig(string Host, string Username, string Password, string VirtualHost, int Port);
    public record RedisConfig(string[] Host, int Port, string Password, int RedisMode);
    public record BusinessDatabaseConfig(int DatabaseSourceType, bool IsReadWriteSplit, string MasterConnectionString, string[] SlaveConnectionString);
    public record MiddlewareDatabaseConfig(int DatabaseSourceType, string SagaConnectionString, string TickerQConnectionString);
    public record DistributedLogConfig(bool IsEnabled, string SeqUrl, string SeqApiKey);
    public record SeqConfig(string Url, string ApiKey);
}
