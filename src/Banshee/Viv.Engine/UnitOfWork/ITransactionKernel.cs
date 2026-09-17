using System.Threading;
using System.Threading.Tasks;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 事务内核 —— 工作单元唯一需要数据库提供的东西，就是一个「开 / 提 / 滚」。
    ///
    /// 【为什么要这一层，而不是直接用 IMomoDbContext】
    /// <c>IMomoDbContext</c> 有 55 个成员，工作单元一个都不用 —— 直连的话单元测试要桩 55 个方法。
    /// 抽出这个只有 3 个方法的窄接口后，测试桩 3 个方法就能覆盖全部事务语义，且完全不碰数据库。
    /// 这就是「Momo 一行不动」的全部代价：一个转发类（<see cref="MomoTransactionAdapter"/>）。
    ///
    /// 【失败语义（照抄 Momo，不做二次包装）】
    /// - <c>BeginAsync</c>：底层实测<b>从不返回 false</b>，失败一律抛 <c>VivConnectionException</c>。
    ///   返回值保留只为接口诚实，调用方仍应防御性判断。
    /// - <c>CommitAsync</c>：失败抛异常 —— 提交失败必须响亮，不能吞。
    /// - <c>RollbackAsync</c>：失败只记日志不抛（与 <c>MomoDatabase.RollbackTransaction</c> 一致），
    ///   避免掩盖触发回滚的那个原始异常。
    /// </summary>
    internal interface ITransactionKernel
    {
        Task<bool> BeginAsync(CancellationToken cancellationToken = default);

        Task CommitAsync(CancellationToken cancellationToken = default);

        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
