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

        /// <summary>
        /// 表示当前操作成功
        /// </summary>
        /// <param name="message"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static FuncResult Success(string? message = "操作成功", object? data = null)
        {
            return new FuncResult { IsSuccess = true, Message = message, Data = data };
        }

        /// <summary>
        /// 失败返回
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static FuncResult Failed(string? message)
        {
            return new FuncResult { IsSuccess = false, Message = message };
        }
    }
}
