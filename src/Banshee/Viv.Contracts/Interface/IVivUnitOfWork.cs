using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 窄事务入口，业务自己划边界。完整事务（方法级 <c>[VivUnitOfWork]</c>）由框架自动开合，不用这个接口。
    ///
    /// <code>
    /// await using var tx = await _unitOfWork.BeginAsync();
    /// await _repo.InsertAsync(x);
    /// await tx.CommitAsync();
    /// </code>
    ///
    /// 从 DI 作用域构造注入 —— 事务状态跟着作用域走，解析到根作用域会让并发请求共用一个事务。
    ///
    /// 事务只针对主库写，读不开事务，业务先把数据备好再开。
    /// 读写未分离时读写共用一条连接，事务内走 Dapper 原生 SQL 读（FindScalar / FindList&lt;T&gt;(sql) / Page 等）会直接抛异常。
    /// </summary>
    public interface IVivUnitOfWork
    {
        /// <summary>
        /// 开启（或加入已开启的）事务。句柄离开作用域时若未提交，自动回滚。
        /// </summary>
        Task<IVivTransaction> BeginAsync(CancellationToken cancellationToken = default);
    }
}
