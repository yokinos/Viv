using System;
using System.Collections.Generic;
using System.Text;
using System.Transactions;
using Viv.Contracts.Model;

namespace Viv.Contracts.Interface
{
    public interface IDistributedTransaction
    {
        /// <summary>
        /// 事务标识（框架内唯一，用于追踪）
        /// </summary>
        string TransactionId { get; }

        /// <summary>
        /// 开启事务
        /// </summary>
        /// <param name="options">事务配置</param>
        void Begin(TransactionOptions options);

        /// <summary>
        /// 注册事务步骤（支持补偿）
        /// </summary>
        /// <param name="step">事务步骤</param>
        void RegisterStep(ITransactionStep step);

        /// <summary>
        /// 提交事务
        /// </summary>
        /// <returns>事务结果</returns>
        TransactionResult Commit();

        /// <summary>
        /// 回滚事务
        /// </summary>
        void Rollback();
    }
}
