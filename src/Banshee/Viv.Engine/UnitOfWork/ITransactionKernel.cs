using System.Threading;
using System.Threading.Tasks;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 事务内核 —— 工作单元唯一需要数据库提供的东西：开 / 提 / 滚。
    ///
    /// 不直接用 <c>IMomoDbContext</c> 是因为它有 55 个成员，工作单元一个都不用，直连的话单元测试要桩 55 个方法。
    /// 窄接口只要桩 3 个方法，代价是一个转发类（<see cref="MomoTransactionAdapter"/>）。
    ///
    /// 失败语义照抄 Momo，不做二次包装：BeginAsync 底层实测从不返回 false，失败一律抛
    /// <c>VivConnectionException</c>，返回值保留只为接口诚实；CommitAsync 失败抛异常，提交失败必须响亮；
    /// RollbackAsync 失败只记日志不抛（与 MomoDatabase.RollbackTransaction 一致），避免掩盖触发回滚的那个原始异常。
    /// </summary>
    internal interface ITransactionKernel
    {
        Task<bool> BeginAsync(CancellationToken cancellationToken = default);

        Task CommitAsync(CancellationToken cancellationToken = default);

        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
