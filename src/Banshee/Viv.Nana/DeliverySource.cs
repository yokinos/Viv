using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Nana
{

    /// <summary>
    /// 投递来源：直接发出 / 发件箱投递器 / 定时任务作业
    /// </summary>
    public enum DeliverySource
    {
        /// <summary>
        /// 由业务代码直接发出
        /// </summary>
        Direct,

        /// <summary>
        /// 由发件箱投递器发出
        /// </summary>
        Outbox,

        /// <summary>
        /// 由定时任务作业发出
        /// </summary>
        Job,
    }
}
