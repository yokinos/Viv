using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Models;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 上下文存取访问器
    /// 负责 AsyncLocal 线程存储，作为底层存储抽象
    /// </summary>
    public interface IVivContextAccessor
    {
        /// <summary>
        /// 获取/设置当前请求上下文快照
        /// </summary>
        VivContextContent? Current { get; set; }
    }
}
