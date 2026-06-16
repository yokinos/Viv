using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    [Serializable]
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

        public static VivApiResult<T> Success(string message = "successful", T? data = default)
        {
            return ApiRsult(ApiResultCode.Success, message, data);
        }

        public static VivApiResult<T> Error(string message, T? data = default)
        {
            return ApiRsult(ApiResultCode.Error, message, data);
        }

        public static VivApiResult<T> ApiRsult(ApiResultCode code, string message, T? data = default)
        {
            return new VivApiResult<T>((int)code, message, data);
        }
    }
}
