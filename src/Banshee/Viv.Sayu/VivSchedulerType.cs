
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Sayu
{
    public enum VivSchedulerType
    {
        /// <summary>
        /// 无需启动定时任务
        /// </summary>
        None,

        /// <summary>
        /// 很有意思的一个调度框架
        /// </summary>
        TickerQ,

        /// <summary>
        /// 还未支持（懒）
        /// </summary>
        QuartzNet,
    }
}
