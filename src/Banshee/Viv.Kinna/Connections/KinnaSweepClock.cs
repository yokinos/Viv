using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 清扫状态。清扫循环每跑完一轮写一次，健康检查读它判断后台任务是否还活着。
    /// 每个 Hub 一个实例。
    /// </summary>
    public sealed class KinnaSweepClock<THub> where THub : Hub
    {
        /// <summary>最近一次清扫完成时间，null 表示还没跑过。</summary>
        public DateTimeOffset? LastSweepAt { get; private set; }

        /// <summary>最近一次清扫清掉了多少连接。</summary>
        public int LastRemoved { get; private set; }

        /// <param name="removed">本轮清掉几条</param>
        /// <param name="at">完成时间，不传就取当前时间；只有测试会传旧时间</param>
        public void Record(int removed, DateTimeOffset? at = null)
        {
            LastSweepAt = at ?? DateTimeOffset.UtcNow;
            LastRemoved = removed;
        }
    }
}
