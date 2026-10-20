namespace Viv.Nana
{
    /// <summary>
    /// 消费者订阅拓扑：队列由同服务多副本共享，还是每实例独占。
    /// 切换会改变队列名、队列类型与持久化方式，旧队列会留在 Broker 上无人消费。
    /// </summary>
    public enum ConsumerTopology
    {
        /// <summary>
        /// 同服务多副本共享一条队列（RabbitMQ 轮询分派），队列名 {EventName}Queue.{ServiceName}。
        /// 同服务只执行一次由 VivConsumer 基类取 Redis 锁保证。
        /// </summary>
        Shared = 0,

        /// <summary>
        /// 每实例独占一条队列，fanout 复制 N 份，队列名 {EventName}Queue.{ServiceName}.{InstanceId}。
        /// 选它时必须把 GetConsumeLockKey 覆写为返回 null：锁 Key 含 ServiceName、全副本相同，
        /// N 个副本抢同一把锁，只有一个能进业务。
        /// </summary>
        PerInstance = 1
    }
}
