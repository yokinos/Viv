using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;

namespace Viv.Contracts.Exceptions
{
    /// <summary>
    /// 通用业务异常
    /// </summary>
    public class VivBusinessException : Exception, IVivBusinessException
    {
        /// <summary>
        /// 发生此异常时返回给前端的错误Code 
        /// </summary>
        public int Code { get; set; } = -200;

        /// <summary>
        /// 发生此异常时返回给前端的数据
        /// </summary>
        public object? Output { get; set; }

        public VivBusinessException(string message) : base(message) { }

        public VivBusinessException(string message, Exception innerException) : base(message, innerException) { }

        public VivBusinessException(string message, int code) : base(message) { Code = code; }

        public VivBusinessException(string message, int code, object output) : base(message) { Code = code; Output = output; }

        public VivBusinessException(string message, int code, Exception innerException) : base(message, innerException) { Code = code; }

        public VivBusinessException(string message, int code, object output, Exception innerException) : base(message, innerException) { Code = code; Output = output; }
    }
}
