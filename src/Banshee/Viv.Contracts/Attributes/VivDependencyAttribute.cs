using System;
using Viv.Contracts.Enums;

namespace Viv.Contracts.Attributes
{
    /// <summary>
    /// 标记 <see cref="Interface.IDependency"/> 实现类的注册方式<br/>
    /// 不标记时默认：AsImplementedInterfaces + Scoped
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class VivDependencyAttribute : Attribute
    {
        public VivDependencyAttribute() { }

        /// <summary>
        /// 生命周期，默认 <see cref="DependencyLifetime.Scoped"/>
        /// </summary>
        public DependencyLifetime Lifetime { get; set; } = DependencyLifetime.Scoped;

        /// <summary>
        /// 是否按具体类型注册（默认 false，即按 AsImplementedInterfaces 注入）
        /// </summary>
        public bool AsSelf { get; set; } = false;

        /// <summary>
        /// 标记
        /// </summary>
        public object? Tag { get; set; }
    }
}
