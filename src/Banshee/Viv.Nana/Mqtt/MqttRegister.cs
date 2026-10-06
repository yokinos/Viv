using Microsoft.Extensions.DependencyInjection;
using Viv.Nana.Options;

namespace Viv.Nana.Mqtt
{
    /// <summary>
    /// MQTT 接入装配。options 为 null = 不启用（与 Nana / Outbox 同一语义）。
    /// 配置实例由 VivConfigLoader 注册（T + IOptions&lt;T&gt;），这里只注册连接与宿主服务。
    /// </summary>
    public static class MqttRegister
    {
        public static IServiceCollection AddVivMqtt(this IServiceCollection services, MqttOptions? options)
        {
            if (options == null) return services;

            services.AddSingleton<MqttConnection>();
            services.AddSingleton<IMqttClientService>(sp => sp.GetRequiredService<MqttConnection>());
            services.AddHostedService<MqttHostedService>();
            return services;
        }
    }
}
