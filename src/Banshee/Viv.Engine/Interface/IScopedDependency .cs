using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Shared.Interface
{
    /// <summary>
    /// 作用域内单例（一次请求/一次操作内唯一）
    /// </summary>
    public interface IScopedDependency: IDependency
    {

    }
}
