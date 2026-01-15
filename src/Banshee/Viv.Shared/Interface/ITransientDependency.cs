using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Shared.Interface
{
    /// <summary>
    ///  瞬时对象（每次获取都新建）
    /// </summary>
    public interface ITransientDependency: IDependency
    {

    }
}
