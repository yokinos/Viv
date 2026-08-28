using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion;
using Viv.Elysia.Request;

namespace Viv.Elysia.Interface
{
    /// <summary>
    /// 定义给各个项目自己的用来自定义请求拦截的约定
    /// </summary>
    public interface IApiRequestCheck
    {
        /// <summary>
        /// 校验请求参数
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<FuncResult> CheckRequestAsync(VivApiRequest request);
    }
}
