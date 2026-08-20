using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Elysia.Interface
{
    /// <summary>
    /// 请求Dto的基类接口
    /// </summary>
    public interface IApiRequest
    {
        /// <summary>
        /// 校验参数
        /// </summary>
        /// <returns></returns>
        string Validate();
    }
}