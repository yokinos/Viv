using System;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 工作单元：把「一段要原子完成的活」交给框架在事务里跑。
    ///
    /// <code>
    /// await _unitOfWork.ExecuteAsync(async ct =>
    /// {
    ///     await _repo.InsertAsync(x, ct);
    ///     await _repo.UpdateAsync(y, ct);
    /// }, cancellationToken: ct);
    /// </code>
    ///
    /// 方法级 <c>[VivUnitOfWork]</c> 由拦截器自动包一层，业务代码不用调本接口。
    ///
    /// 语义：
    /// <list type="bullet">
    /// <item>work 正常返回、且 <c>commitWhen</c> 判据通过（默认通过）→ 提交；</item>
    /// <item>work 抛异常，或 <c>commitWhen</c> 返回 false → 整笔回滚（后者用于「业务返回失败信封」）；</item>
    /// <item>嵌套调用并入外层事务，不重开、不单独提交（没有保存点，内层失败会拖垮整笔）；</item>
    /// <item>事务只针对主库写。</item>
    /// </list>
    ///
    /// 与旧的两段式调用（Begin/Commit/Rollback）的区别：没有事务句柄，边界就是 lambda 的生存期。
    /// 这不是风格选择 —— EF 的执行策略必须在失败时把整笔重做，所以 BEGIN/COMMIT 得由它拥有，
    /// 用户自己开的事务它一律拒绝。
    ///
    /// 从 DI 作用域构造注入：事务状态跟着作用域走，解析到根作用域会让并发请求共用一个事务。
    /// </summary>
    public interface IVivUnitOfWork
    {
        /// <summary>
        /// 在事务里执行 <paramref name="work"/>。
        /// </summary>
        /// <param name="work">要原子完成的工作</param>
        /// <param name="commitWhen">提交判据：拿到 work 的返回值，返回 false 则回滚（默认总是提交）</param>
        /// <param name="cancellationToken">只用于开事务；提交/回滚不跟它走（收尾动作不应被客户端断开取消）</param>
        Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> work,
            Func<T, bool>? commitWhen = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 无返回值的版本：正常返回即提交，抛异常即回滚。
        /// </summary>
        Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
    }
}
