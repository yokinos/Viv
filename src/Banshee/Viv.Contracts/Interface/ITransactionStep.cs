using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Interface
{
    public interface ITransactionStep
    {
        /// <summary>
        /// 步骤标识
        /// </summary>
        string StepId { get; }

        /// <summary>
        /// 执行逻辑
        /// </summary>
        /// <returns>是否成功</returns>
        bool Execute();

        /// <summary>
        /// 补偿逻辑（Saga必备，DTC可空）
        /// </summary>
        void Compensate();
    }
}
