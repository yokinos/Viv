using System;
using System.Collections.Concurrent;
using System.Text;
using Viv.Vva.Snowflake;

namespace Viv.Vva.Magic
{
    public static class IdMagic
    {
        private static readonly ConcurrentDictionary<long, LeafSnowflakeIdGenerator> _idDict = new();

        /// <summary>
        /// 返回基于 options.MachineId 的雪花 ID。相同 MachineId 对应同一个生成器实例（第一次传入的 options 用于初始化该实例）。
        /// </summary>
        public static long NextId(SnowflakeOptions options)
        {
            var opt = options ?? new SnowflakeOptions();
            var machineId = opt.MachineId;
            var generator = _idDict.GetOrAdd(machineId, _ => new LeafSnowflakeIdGenerator(opt));
            return generator.NextId();
        }

        /// <summary>
        /// 直接按机器 ID
        /// </summary>
        public static long NextId(long machineId = 1)
        {
            var generator = _idDict.GetOrAdd(machineId, id => new LeafSnowflakeIdGenerator(new SnowflakeOptions { MachineId = id }));
            return generator.NextId();
        }

        /// <summary>
        /// 移除指定 machineId 对应的生成器（比如释放资源或重新配置时使用）。
        /// </summary>
        public static bool RemoveGenerator(long machineId) => _idDict.TryRemove(machineId, out _);
    }
}
