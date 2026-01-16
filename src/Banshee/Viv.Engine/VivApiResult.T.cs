using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    public class VivApiResult<T> : VivApiResult
    {
        public VivApiResult() { }
        public VivApiResult(int code, string message) : this(code, message, default) { }
        public VivApiResult(int code, string message, T? data)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        /// <summary>
        /// 数据
        /// </summary>
        public new T? Data { get; set; }
    }
}
