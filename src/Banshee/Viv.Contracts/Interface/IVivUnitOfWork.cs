using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 工作单元入口 —— 窄事务模式的起点。<br/>
    /// 完整事务（方法级 <c>[VivUnitOfWork]</c> 特性 + 拦截）由框架自动开合，业务不需要碰这个接口。
    ///
    /// <code>
    /// await using var tx = await _unitOfWork.BeginAsync();
    /// await _repo.InsertAsync(x);
    /// await tx.CommitAsync();
    /// </code>
    ///
    /// ⚠️ <b>必须从 DI 作用域解析</b>（构造注入）。事务状态跟着作用域走 —— 解析到根作用域的实现会让
    /// 并发请求共用同一个事务，灾难性后果。
    ///
    /// 实现基于 <c>IMomoDbContext</c> 的 Begin/Commit/Rollback，不改变它们的语义。
    ///
    /// ⚠️ <b>事务只针对主库写，读不开事务</b> —— 业务要先把数据备好，再开事务。当前读写未分离
    /// （<c>IsReadWriteSplit: false</c>）时读写共用一条连接，事务内走 Dapper 的原生 SQL 读
    /// （<c>FindScalar</c> / <c>FindList&lt;T&gt;(sql)</c> / <c>Page</c> 等）会<b>直接抛异常</b>而非读到脏数据。
    /// </summary>
    public interface IVivUnitOfWork
    {
        /// <summary>
        /// 开启（或加入已开启的）事务。返回的句柄离开作用域时若未 <c>CommitAsync</c>，自动回滚。
        /// </summary>
        Task<IVivTransaction> BeginAsync(CancellationToken cancellationToken = default);
    }
}
