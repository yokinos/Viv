using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Exceptions
{
    /// <summary>
    /// 令牌无效异常（统一异常类型）
    /// </summary>
    public class InvalidTokenException : Exception
    {
        public InvalidTokenException(string message) : base(message) { }
        public InvalidTokenException(string message, Exception innerException) : base(message, innerException) { }
    }
}
