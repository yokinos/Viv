using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Vva
{
    /// <summary>
    /// QPS 限流器
    /// </summary>
    public class QpsRateLimiter
    {
        private readonly SemaphoreSlim _semaphore = new(1);
        private readonly long _intervalTicks; // 每次请求间隔的时钟周期数
        private long _nextAvailableTicks;    // 下一次允许通过的时钟周期

        public QpsRateLimiter(double qps)
        {
            if (qps <= 0) throw new ArgumentOutOfRangeException(nameof(qps));

            // 核心算法：1秒 / QPS = 间隔秒数 -> 转为 Ticks
            // 全程只计算一次，避免每次 Wait 都算浮点数
            double intervalSeconds = 1.0 / qps;
            _intervalTicks = (long)(intervalSeconds * Stopwatch.Frequency);

            // 初始化为当前时间，保证第一个请求立即通过
            _nextAvailableTicks = Stopwatch.GetTimestamp();
        }

        public async Task WaitAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                long now = Stopwatch.GetTimestamp();

                // 计算需要“睡”多久
                long waitTicks = _nextAvailableTicks - now;
                if (waitTicks > 0)
                {
                    double waitMs = (double)waitTicks / Stopwatch.Frequency * 1000;
                    await Task.Delay(TimeSpan.FromMilliseconds(waitMs));
                }

                // 基于“计划时间”累加，而不是“当前时间”
                // 这样能保证即使某次 Delay 稍微慢了一点，整体节奏依然是匀速的
                _nextAvailableTicks = Math.Max(now, _nextAvailableTicks) + _intervalTicks;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}