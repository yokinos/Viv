using System;

namespace Viv.Contracts.Attributes
{
    /// <summary>
    /// 工作单元 —— 把整个方法包成一个数据库事务：进方法开启，方法正常结束提交，抛异常或返回失败信封则回滚。
    ///
    /// 【两种模式，业务自行取舍】
    /// - <b>完整事务（本特性）</b>：一个方法就是一次业务操作，业务代码里看不到事务。<br/>
    /// - <b>窄事务</b>：拿 <see cref="Interface.IVivUnitOfWork"/> 自己 <c>BeginAsync()</c>，
    ///   <c>await using</c> 自动释放 —— 只想包住几行写操作、或需要在事务里夹非数据库动作时用。
    ///
    /// 【用法】API 侧标在<b>方法</b>上；Worker 侧（消费者）标在<b>类</b>上，由消费者基类读取。
    ///
    /// 【四条硬要求 —— 前三条启动期会硬报错，第四条只能靠自觉】
    /// 1. <b>方法必须返回 <c>Task</c> / <c>Task&lt;T&gt;</c>（或 <c>ValueTask&lt;T&gt;</c>）</b>。同步方法和
    ///    <b>非泛型 <c>ValueTask</c></b> 都走异步拦截链之外，实测结果是「方法照常执行、事务根本没开」
    ///    —— 静默失效，所以启动期直接拦住。
    /// 2. <b>方法必须 <c>public virtual</c></b>。接口代理只能拦虚方法，非虚 / static / private 一律拦不到。
    /// 3. <b>所在类型必须由 DIOption 扫成 <c>AsImplementedInterfaces</c></b>。
    ///    <c>[VivDependency(AsSelf = true)]</c> 注册的类型没有接口，生成不出代理。
    /// 4. <b>不要靠「同类里的自调用」触发</b>（<c>this.OtherMethod()</c>）。自调用走的是真实实例、
    ///    不过代理，标了也不生效。要包事务就把特性标在<b>最外层那个公开方法</b>上。
    ///
    /// 【失败判定】方法返回 <c>VivApiResult</c> 且信封码不在 2xx 区间 → 回滚（与本地事件分发
    /// 的成败判定同源，见 <c>FailDetector</c>）。异常 → 回滚后原样上抛，不吞。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class VivUnitOfWorkAttribute : Attribute
    {
        public VivUnitOfWorkAttribute() { }

        /// <summary>
        /// 是否启用。置 false 可用于「类上标了、个别方法要豁免」的场景 ——
        /// 拦截器读到 false 直接放行，不开事务。
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
