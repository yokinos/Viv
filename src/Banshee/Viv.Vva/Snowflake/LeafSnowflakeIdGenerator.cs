using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Viv.Vva.Snowflake
{
    public class LeafSnowflakeIdGenerator
    {
        private readonly SnowflakeOptions _options;

        private readonly long _machineIdMask; // 机器ID最大值掩码
        private readonly long _sequenceMask;  // 序列号最大值掩码
        private readonly int _timestampShift; // 时间戳左移位数（作为 shiftCount 必须为 int）
        private readonly int _machineIdShift; // 机器ID左移位数（作为 shiftCount 必须为 int）

        private long _lastTimestamp = -1L;     // 上一次生成ID的时间戳
        private long _sequence = 0L;           // 当前序列号
        private readonly object _lockObj = new(); // 线程安全锁

        public LeafSnowflakeIdGenerator([AllowNull]SnowflakeOptions options)
        {
            _options = options ?? new SnowflakeOptions();

            if (_options.MachineIdBits < 0 || _options.MachineIdBits >= 63)
                throw new ArgumentOutOfRangeException(nameof(options.MachineIdBits), "MachineIdBits 必须在 0-62 范围内");
            if (_options.SequenceBits < 0 || _options.SequenceBits >= 63)
                throw new ArgumentOutOfRangeException(nameof(options.SequenceBits), "SequenceBits 必须在 0-62 范围内");
            if (_options.MachineIdBits + _options.SequenceBits >= 63)
                throw new ArgumentException("MachineIdBits + SequenceBits 必须小于 63，以便保留给时间戳位数");

            // 使用 long 位移，避免 int 溢出；掩码计算使用 1L<<bits
            _machineIdMask = (1L << _options.MachineIdBits) - 1L;
            if (_options.MachineId < 0 || _options.MachineId > _machineIdMask)
            {
                throw new ArgumentOutOfRangeException(nameof(options.MachineId), $"机器ID必须在0-{_machineIdMask}之间（当前配置位数：{_options.MachineIdBits}）");
            }

            // 计算位移和掩码（shift 计数必须为 int）
            _sequenceMask = (1L << _options.SequenceBits) - 1L;
            _machineIdShift = (int)_options.SequenceBits;
            _timestampShift = (int)(_options.SequenceBits + _options.MachineIdBits);
        }

        public long NextId()
        {
            lock (_lockObj)
            {
                long currentTimestamp = GetCurrentTimestamp();

                #region 时钟回拨处理

                if (currentTimestamp < _lastTimestamp)
                {
                    long backwardMs = _lastTimestamp - currentTimestamp;
                    // 1. 若回拨时间在容忍范围内，等待时钟追平
                    if (backwardMs <= _options.MaxClockBackwardMs)
                    {
                        Console.WriteLine($"检测到时钟回拨{backwardMs}ms，等待时钟追平...");
                        currentTimestamp = WaitUntilTimestamp(_lastTimestamp);
                    }
                    // 2. 超过容忍时间，直接抛异常（避免ID重复）
                    else
                    {
                        throw new InvalidOperationException($"时钟回拨超出容忍范围：{backwardMs}ms（最大容忍：{_options.MaxClockBackwardMs}ms）");
                    }
                }

                #endregion

                #region 序列号处理

                if (currentTimestamp == _lastTimestamp)
                {
                    // 同一毫秒内，序列号自增
                    _sequence = (_sequence + 1) & _sequenceMask;
                    // 序列号溢出，等待下一个毫秒
                    if (_sequence == 0)
                    {
                        currentTimestamp = WaitNextMillisecond(_lastTimestamp);
                    }
                }
                else
                {
                    // 新毫秒，重置序列号
                    _sequence = 0L;
                }

                #endregion

                // 更新最后生成时间戳
                _lastTimestamp = currentTimestamp;

                // 拼接ID：时间戳 + 机器ID + 序列号
                return ((currentTimestamp - _options.Twepoch) << _timestampShift) | ((_options.MachineId & _machineIdMask) << _machineIdShift) | _sequence;
            }
        }

        /// <summary>
        /// 获取当前UTC毫秒时间戳
        /// </summary>
        private static long GetCurrentTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// 等待到下一个毫秒（使用 SpinWait 以减少 Thread.Sleep 导致的过度等待）
        /// </summary>
        private static long WaitNextMillisecond(long lastTimestamp)
        {
            long timestamp = GetCurrentTimestamp();
            var sw = new SpinWait();
            while (timestamp <= lastTimestamp)
            {
                // 先自旋几次以快速通过小延迟，然后短暂让出，权衡延迟与 CPU 使用
                sw.SpinOnce();
                if (sw.NextSpinWillYield)
                {
                    Thread.Sleep(0);
                }
                timestamp = GetCurrentTimestamp();
            }
            return timestamp;
        }

        /// <summary>
        /// 等待直到时间戳 >= 目标时间戳（时钟回拨修复用）
        /// </summary>
        private static long WaitUntilTimestamp(long targetTimestamp)
        {
            long timestamp = GetCurrentTimestamp();
            var sw = new SpinWait();
            while (timestamp < targetTimestamp)
            {
                sw.SpinOnce();
                if (sw.NextSpinWillYield)
                {
                    Thread.Sleep(0);
                }
                timestamp = GetCurrentTimestamp();
            }
            return timestamp;
        }
    }
}
