using Microsoft.Extensions.Diagnostics.HealthChecks;
using Viv.Nana.Mqtt;

namespace Viv.Engine.HealthChecks
{
    /// <summary>
    /// MQTT 连接健康检查。判据只是「当前是否连线」，不主动探测 ——
    /// ManagedClient 自己在重连，这里只如实报告状态，别把重连中的正常抖动判成故障。
    /// </summary>
    public class MqttHealthCheck : IHealthCheck
    {
        private readonly IMqttClientService _mqtt;

        public MqttHealthCheck(IMqttClientService mqtt) => _mqtt = mqtt;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(_mqtt.IsConnected
                ? HealthCheckResult.Healthy("MQTT 已连接")
                : HealthCheckResult.Unhealthy("MQTT 未连接"));
    }
}
