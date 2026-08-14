using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 依赖注入标记接口
    /// 打上该标记的类型，配合 <see cref="Attributes.VivDependencyAttribute"/> 特性，实现自动注册到容器，控制注入生命周期。
    /// </summary>
    public interface IDependency
    {

    }
}