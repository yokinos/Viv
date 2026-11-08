using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 清扫状态。清扫循环每跑完一轮写一次，健康检查读它判断后台任务是否还活着。
    /// 每个 Hub 一个实例。
    ///
    /// 时间戳存成 long（UTC ticks）而不是 <see cref="DateTimeOffset"/>?：写的一方在清扫线程，
    /// 读的一方在健康检查线程，16 字节的 struct 赋值不是原子的，不加锁就可能读到半新半旧的时刻。
    /// long 在 64 位上读写的原子性由运行时保证，配 <see cref="Volatile"/> 兼顾可见性。
    /// </summary>
    public sealed class KinnaSweepClock<THub> where THub : Hub
    {
        private readonly TimeProvider _time;

        // 0 表示「还没跑过」：UtcNow 永远不可能是公元 1 年
        private long _lastSweepTicks;
        private int _lastRemoved;

        public KinnaSweepClock(TimeProvider? timeProvider = null)
        {
            _time = timeProvider ?? TimeProvider.System;
        }

        /// <summary>最近一次清扫完成时间，null 表示还没跑过。</summary>
        public DateTimeOffset? LastSweepAt
        {
            get
            {
                var ticks = Volatile.Read(ref _lastSweepTicks);
                return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        /// <summary>最近一次清扫清掉了多少连接。</summary>
        public int LastRemoved => Volatile.Read(ref _lastRemoved);

        /// <param name="removed">本轮清掉几条</param>
        public void Record(int removed)
        {
            Volatile.Write(ref _lastSweepTicks, _time.GetUtcNow().UtcTicks);
            Volatile.Write(ref _lastRemoved, removed);
        }
    }
}
