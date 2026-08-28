using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Extension;

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

        public new T? Data { get; set; }

        public static VivApiResult<T> Success(string? message, T? data = default)
        {
            return ApiResult(ApiResultCode.Success, message, data);
        }

        public static VivApiResult<T> Success(T? data = default)
        {
            return ApiResult(ApiResultCode.Success, "successful", data);
        }

        public static VivApiResult<T> Failed(string? message, T? data = default)
        {
            return ApiResult(ApiResultCode.Error, message, data);
        }

        public static VivApiResult<T> ApiResult(ApiResultCode code, string? message = null, T? data = default)
        {
            message ??= code.GetDescription();
            return new VivApiResult<T>((int)code, message, data);
        }
    }
}
