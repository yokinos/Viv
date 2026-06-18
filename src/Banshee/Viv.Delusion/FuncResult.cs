using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Delusion
{
    /// <summary>
    /// 函数返回结果
    /// </summary>
    public class FuncResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 返回数据
        /// </summary>
        public object? Data { get; set; }

        public static FuncResult Success(string? msg = "操作成功", object? data = null)
        {
            return new FuncResult { IsSuccess = true, Message = msg, Data = data };
        }

        public static FuncResult Fail(string msg)
        {
            return new FuncResult { IsSuccess = false, Message = msg };
        }
    }
}
