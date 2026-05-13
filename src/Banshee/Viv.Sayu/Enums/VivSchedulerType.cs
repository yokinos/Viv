
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Sayu.Enums
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
        /// [预留]非必要不打算实现
        /// </summary>
        QuartzNet,
    }
}
