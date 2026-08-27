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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static FuncResult<T> Success(string? message, T? data = default)
        {
            return new FuncResult<T> { IsSuccess = true, Message = message, Data = data };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static FuncResult<T> Success(T? data = default)
        {
            return new FuncResult<T> { IsSuccess = true, Message = "Successful", Data = data };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static FuncResult<T> Failed(string? message)
        {
            return new FuncResult<T> { IsSuccess = false, Message = message };
        }
    }
}
