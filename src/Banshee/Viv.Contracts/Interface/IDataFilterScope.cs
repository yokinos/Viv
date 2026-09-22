using System;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 数据过滤器作用域：一次关掉多个过滤器，释放时整份恢复
    ///
    /// 与 <see cref="IDataFilter.Disable{TFilter}"/> 的区别只在「关多个」的时候。那个每条返回一个句柄，
    /// 叠着用必须逆序释放，先释放外层会把快照写回成外层进入前的样子，内层就白关了。
    /// 这个只有一个句柄，关几条、按什么顺序关都不影响释放结果。
    ///
    /// 关掉的动作在调用当下立刻生效，不是攒到释放时统一应用；释放只负责恢复。
    /// </summary>
    public interface IDataFilterScope : IDisposable
    {
        /// <summary>
        /// 关掉 TFilter 对应的过滤器，返回自身以便连着写
        /// </summary>
        IDataFilterScope Disable<TFilter>() where TFilter : class;

        /// <summary>
        /// 同上，按类型关。手上是一组运行期拼出来的 Type 时用这个
        /// </summary>
        IDataFilterScope Disable(Type filterType);
    }
}
