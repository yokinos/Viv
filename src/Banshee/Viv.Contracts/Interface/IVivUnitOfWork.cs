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
    /// 嵌套：只有最外层那次 BeginAsync 真开数据库事务，嵌套调用拿到的是子句柄。
    /// 子句柄的提交是空操作；子句柄没提交就释放（含抛异常）→ 整个作用域被标记 rollback-only，
    /// 最外层再提交也会降级成回滚并记一条 Warning。没有保存点，内层回滚会拖垮整个事务，不会只撤销内层那几行。
    ///
    /// 从 DI 作用域构造注入 —— 事务状态跟着作用域走，解析到根作用域会让并发请求共用一个事务。
    /// 也正因为状态绑在作用域上，同一作用域内只可能有一个事务：要一段与当前事务互不影响的操作，
    /// 得从新建的 DI 作用域解析本接口（IServiceScopeFactory.CreateScope），那条路径走独立连接 ——
    /// 内层与外层写同一批行会死锁。
    ///
    /// 事务只针对主库写，读不开事务，业务先把数据备好再开。
    /// 读写未分离时读写共用一条连接，事务内走 Dapper 原生 SQL 读（FindScalar / FindList&lt;T&gt;(sql) / Page 等）会直接抛异常。
    /// </summary>
    public interface IVivUnitOfWork
    {
        /// <summary>
        /// 开启事务；若当前作用域已有未结束的事务，则加入它并返回虚拟子句柄，不再新开数据库事务。
        /// 句柄离开作用域时若未提交，自动回滚。
        /// </summary>
        Task<IVivTransaction> BeginAsync(CancellationToken cancellationToken = default);
    }
}
