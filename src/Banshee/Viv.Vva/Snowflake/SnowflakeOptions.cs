using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Vva.Snowflake
{
    public class SnowflakeOptions
    {
        /// <summary>
        /// 起始时间戳（毫秒）
        /// </summary>
        public long Twepoch { get; set; } = 1609459200000L;

        /// <summary>
        /// 机器ID位数（默认10位，支持1024台机器）
        /// </summary>
        public int MachineIdBits { get; set; } = 10;

        /// <summary>
        /// 序列号位数（默认12位，每毫秒4096个ID）
        /// </summary>
        public int SequenceBits { get; set; } = 12;

        /// <summary>
        /// 最大时钟回拨容忍时间（毫秒），超过则抛异常
        /// </summary>
        public long MaxClockBackwardMs { get; set; } = 5;

        /// <summary>
        /// 机器ID（从配置/数据库/注册中心读取）
        /// </summary>
        public long MachineId { get; set; } = 1;
    }
}
