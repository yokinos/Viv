using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity
{
    /// <summary>
    /// 请求Dto的基类接口
    /// </summary>
    public interface IApiRequest
    {
        /// <summary>
        /// 校验请求参数
        /// </summary>
        /// <returns></returns>
        string Validate();
    }
}
