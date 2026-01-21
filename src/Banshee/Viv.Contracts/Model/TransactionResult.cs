using System;
using System.Collections.Generic;
using System.Text;


namespace Viv.Contracts.Model
{
    /// <summary>
    /// 事务执行结果
    /// </summary>
    public class TransactionResult
    {
        public bool Success { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }
}
