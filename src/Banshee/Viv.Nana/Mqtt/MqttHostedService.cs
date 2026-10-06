using Microsoft.Extensions.Hosting;

namespace Viv.Nana.Mqtt
{
    /// <summary>宿主启动时连 broker，停机时断开。连接失败不阻塞启动 —— ManagedClient 会自己重连。</summary>
    internal sealed class MqttHostedService : IHostedService
    {
        private readonly MqttConnection _connection;

        public MqttHostedService(MqttConnection connection) => _connection = connection;

        public Task StartAsync(CancellationToken cancellationToken) => _connection.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => _connection.StopAsync(cancellationToken);
    }
}
