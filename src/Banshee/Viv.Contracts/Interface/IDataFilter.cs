using System;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 数据过滤器开关
    /// 在作用域内临时关掉某个全局查询过滤器，供「确实要读到被过滤掉的行」的场景使用。
    ///
    /// 泛型参数是过滤器类本身（Momo 侧 IMomoDataFilter 的实现，如 SoftDeletedFilter / TenantDataFilter），
    /// 一个类就是一个过滤器。加过滤器只需实现接口并挂进清单，这个接口一个字都不用改。
    /// </summary>
    public interface IDataFilter
    {
        /// <summary>
        /// 关掉 TFilter 对应的过滤器。返回的句柄释放时恢复进入之前的取值，可嵌套。
        /// 关掉租户过滤会让查询跨租户，只在自己清楚要读全量数据时用。
        /// </summary>
        IDisposable Disable<TFilter>() where TFilter : class;

        /// <summary>
        /// TFilter 对应的过滤器当前是否被关掉
        /// </summary>
        bool IsDisabled<TFilter>() where TFilter : class;

        /// <summary>
        /// 开一个作用域，在里面接连关掉多个过滤器，释放时一起恢复。
        /// 只关一条用 Disable&lt;TFilter&gt;() 就够了，多条用这个省去句柄的释放顺序
        /// </summary>
        IDataFilterScope Scope();
    }
}
