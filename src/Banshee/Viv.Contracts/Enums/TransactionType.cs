using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Enums
{
    /// <summary>
    /// 事务类型
    /// </summary>
    public enum TransactionType
    {
        /// <summary>
        /// 本地事务
        /// </summary>
        Local,

        /// <summary>
        /// WCF DTC强一致事务
        /// </summary>
        WcfDtc,

        /// <summary>
        /// Saga最终一致事务
        /// </summary>
        Saga,

        /// <summary>
        /// gRPC+消息队列事务
        /// </summary>
        GrpcMq
    }
}
