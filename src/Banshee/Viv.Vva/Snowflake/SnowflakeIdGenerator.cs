
using System;
using System.Threading;

namespace Viv.Vva.Snowflake
{
    /// <summary>
    /// 经典 Snowflake 算法实现
    /// </summary>
    public sealed class SnowflakeIdGenerator
    {
        private const long Epoch = 1288834974657L; // Twitter 原始起点：2010-11-04 01:42:54

        // 位段长度（64位二进制总长度）
        private const int SignBit = 1;          // 符号位（固定0，占1位）
        private const int TimestampBits = 41;   // 时间戳（毫秒级，占41位）
        private const int MachineIdBits = 10;   // 机器ID（占10位，支持0-1023节点）
        private const int SequenceBits = 12;    // 序列号（占12位，每毫秒最多4096个ID）

        // 最大值计算（使用 long，避免位移在 32 位阈值处出错）
        private const long MaxMachineId = (1L << MachineIdBits) - 1L; // 1023
        private const long MaxSequence = (1L << SequenceBits) - 1L;   // 4095

        // 位移量（shift 计数为 int）
        private const int MachineIdShift = SequenceBits;                     // 机器ID左移12位
        private const int TimestampShift = SequenceBits + MachineIdBits;     // 时间戳左移22位

        private readonly long _machineId;        // 当前节点机器ID（实例私有）
        private long _lastTimestamp = -1L;       // 上一次生成ID的时间戳（毫秒）
        private long _sequence = 0L;             // 毫秒内序列号
        private readonly object _lock = new();   // 实例级锁，避免不同实例间不必要的争用

        /// <summary>
        /// 构造函数（指定机器ID）
        /// </summary>
        /// <param name="machineId">机器ID（0-1023），分布式部署时每个节点唯一</param>
        /// <exception cref="ArgumentException">机器ID超出范围时抛出</exception>
        public SnowflakeIdGenerator(long machineId)
        {
            if (machineId < 0 || machineId > MaxMachineId)
            {
                throw new ArgumentException($"机器ID必须在0-{MaxMachineId}之间", nameof(machineId));
            }

            _machineId = machineId;
        }

        /// <summary>
        /// 生成 64 位雪花 ID （long）
        /// </summary>
        public long NextId()
        {
            lock (_lock)
            {
                long currentTimestamp = GetCurrentUtcTimestamp();

                // 处理时钟回拨（拒绝生成以避免重复）
                if (currentTimestamp < _lastTimestamp)
                {
                    throw new InvalidOperationException($"时钟回拨检测，拒绝生成ID。上一次时间：{_lastTimestamp}，当前时间：{currentTimestamp}，差值：{_lastTimestamp - currentTimestamp}ms");
                }

                if (currentTimestamp == _lastTimestamp)
                {
                    // 同一毫秒内递增序列号并使用掩码截断
                    _sequence = (_sequence + 1) & MaxSequence;

                    // 序列溢出，等待下一毫秒
                    if (_sequence == 0)
                    {
                        currentTimestamp = WaitUntilNextMillisecond(_lastTimestamp);
                    }
                }
                else
                {
                    // 新毫秒，重置序列号
                    _sequence = 0;
                }

                _lastTimestamp = currentTimestamp;

                // 组装 ID：时间戳段 | 机器段 | 序列段
                long snowflakeId = ((currentTimestamp - Epoch) << TimestampShift) | (_machineId << MachineIdShift) | _sequence;
                return snowflakeId;
            }
        }

        /// <summary>
        /// 获取当前 UTC 毫秒时间戳
        /// </summary>
        private static long GetCurrentUtcTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// 等待直到下一毫秒（当序列用尽时调用）
        /// 使用 SpinWait + Thread.Sleep(0) 来平衡延迟和 CPU 占用。
        /// </summary>
        private static long WaitUntilNextMillisecond(long lastTimestamp)
        {
            long timestamp = GetCurrentUtcTimestamp();
            var sw = new SpinWait();
            while (timestamp <= lastTimestamp)
            {
                sw.SpinOnce();
                if (sw.NextSpinWillYield)
                {
                    Thread.Sleep(0);
                }
                timestamp = GetCurrentUtcTimestamp();
            }
            return timestamp;
        }
    }
}
