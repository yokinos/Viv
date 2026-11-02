using System;

namespace Viv.Contracts.Attributes
{
    /// <summary>
    /// 工作单元 —— 整个方法包成一个事务：进方法开，正常返回提交，抛异常或返回失败信封则回滚。
    /// 想在业务代码里自己划边界、或要在事务中夹非数据库动作，改用窄事务 <see cref="Interface.IVivUnitOfWork"/>。
    ///
    /// 标在实现类的 public 方法上，不要标接口（接口上标了不生效，且类型级根本标不上）。
    /// 代理形态看类型有没有业务接口：
    /// <list type="bullet">
    /// <item><description>有业务接口 → Castle 接口代理。不要求 virtual，非 virtual（含隐式实现接口的
    /// <c>virtual final</c>）同样能拦到。</description></item>
    /// <item><description>没有业务接口 → Castle 类代理（AsSelf + EnableClassInterceptors）。
    /// 被拦方法必须是 <c>public virtual</c>，类型必须 public、非 sealed、有 public 构造函数；
    /// 否则注册期直接启动失败。</description></item>
    /// </list>
    /// 方法须返回 Task / Task&lt;T&gt;，非泛型 ValueTask、ValueTask&lt;T&gt; 与同步方法都拦不到。
    /// 类级特性与方法级同一把尺子：同步方法会让启动失败；
    /// 开放泛型类型上标了同样启动失败。想让个别方法豁免，在那个方法上标 <c>Enabled = false</c>；
    /// 想让整个类豁免，标在类上 —— 派生类去掉继承来的特性也只有这一个办法。
    /// 所在类型须由 DIOption 扫描注册（<c>[VivDependency(AsSelf = true)]</c> 那条路不挂代理）。
    /// 消费者（VivConsumer / VivLocalConsumer）不走代理：HandleAsync 按本特性显式开合事务。
    /// 接口代理下自调用 <c>this.OtherMethod()</c> 不过代理；类代理下会按虚派发重新进拦截器
    /// （嵌套开子事务，提交是 no-op）。事务性方法尽量标在最外层公开方法上。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class VivUnitOfWorkAttribute : Attribute
    {
        public VivUnitOfWorkAttribute() { }

        /// <summary>
        /// 是否启用。标在方法上是豁免那一个方法，标在类上是豁免整个类。
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
