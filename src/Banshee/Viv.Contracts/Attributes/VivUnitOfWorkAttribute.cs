using System;

namespace Viv.Contracts.Attributes
{
    /// <summary>
    /// 工作单元 —— 整个方法包成一个事务：进方法开，正常返回提交，抛异常或返回失败信封则回滚。
    /// 想在业务代码里自己划边界、或要在事务中夹非数据库动作，改用窄事务 <see cref="Interface.IVivUnitOfWork"/>。
    ///
    /// 标在实现类的 public virtual 方法上，不要标接口（接口上标了不生效，且类型级根本标不上）。
    /// 方法须返回 Task / Task&lt;T&gt; / ValueTask&lt;T&gt;，非泛型 ValueTask 与同步方法都拦不到。
    /// 类级特性与方法级同一把尺子：不可重写（含隐式接口实现的 virtual+final）或同步方法会让启动失败；
    /// 开放泛型类型上标了同样启动失败。想让个别方法豁免，标 <c>Enabled = false</c>。
    /// 所在类型须由 DIOption 按接口注册（AsSelf 注册的没有接口，生成不出代理）。
    /// 消费者（VivConsumer / VivLocalConsumer）不走接口代理：HandleAsync 按本特性显式开合事务。
    /// 自调用 this.OtherMethod() 不过代理，特性标在最外层公开方法上。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class VivUnitOfWorkAttribute : Attribute
    {
        public VivUnitOfWorkAttribute() { }

        /// <summary>
        /// 是否启用。类上标了想让个别方法豁免时置 false。
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
