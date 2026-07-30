using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Delusion
{
    /// <summary>
    /// 函数返回结果
    /// </summary>
    public class FuncResult<T> : FuncResult
    {
        /// <summary>
        /// 返回数据(泛型)
        /// </summary>
        public new T? Data { get; set; }

        public static FuncResult<T> Success(string? msg = "操作成功", T? data = default)
        {
            return new FuncResult<T> { IsSuccess = true, Message = msg, Data = data };
        }

        public static FuncResult<T> Failed(string msg)
        {
            return new FuncResult<T> { IsSuccess = false, Message = msg };
        }


    }
}
