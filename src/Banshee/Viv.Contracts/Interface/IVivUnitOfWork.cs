using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 窄事务入口，由业务自行划定事务边界。完整方法级事务（<c>[VivUnitOfWork]</c> 特性）由框架自动管理，无需使用本接口。
    ///
    /// <code>
    /// await using var tx = await _unitOfWork.BeginAsync();
    /// await _repo.InsertAsync(x);
    /// await tx.CommitAsync();
    /// </code>
    ///
    /// <list type="bullet">
    /// <item><description>嵌套规则：仅最外层那次 BeginAsync 真正开启数据库事务，嵌套调用拿到的是虚拟子句柄。子句柄的提交是空操作；子句柄未提交就释放（含抛出异常）则整个作用域标记 rollback-only，最外层再提交也会降级为回滚并记一条警告。不使用数据库保存点 —— 内层回滚会拖垮整个事务，不会只撤销内层的写入。</description></item>
    /// <item><description>DI生命周期：从 DI 作用域注入，事务状态跟随当前作用域；若注册为根作用域，并发请求会共享同一个状态机 —— 一个请求提交会把另一个请求的事务也提交掉。</description></item>
    /// <item><description>独立事务：状态绑定在作用域上，同一作用域内只可能存在一个事务。需要与当前事务互不影响的事务，从新建的 DI 作用域解析本接口（IServiceScopeFactory.CreateScope）—— 那条路径走独立连接，注意内层与外层写同一批行会死锁。</description></item>
    /// <item><description>读写约定：事务仅用于主库写入，查询不开启事务；业务先把数据备好，再开启事务。</description></item>
    /// <item><description>读写混合限制：读写未分离场景下，事务内使用 Dapper 原生 SQL 查询（FindScalar / FindList&lt;T&gt;(sql) / Page 等）会直接抛出异常。</description></item>
    /// </list>
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
