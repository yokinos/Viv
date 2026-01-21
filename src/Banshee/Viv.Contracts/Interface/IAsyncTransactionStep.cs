using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Interface
{
    public interface IAsyncTransactionStep
    {
        /// <summary>
        /// 步骤标识
        /// </summary>
        string StepId { get; }

        /// <summary>
        /// 异步执行逻辑
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> ExecuteAsync();

        /// <summary>
        /// 异步补偿逻辑（Saga必备）
        /// </summary>
        /// <returns>异步任务</returns>
        Task CompensateAsync();
    }
}
