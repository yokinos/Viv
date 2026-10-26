using System;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 事务内核 —— 工作单元唯一需要数据库提供的东西：把一段活放进事务里跑。
    ///
    /// 不直接用 <c>IMomoDbContext</c> 是因为它有五十多个成员，工作单元一个都不用，
    /// 直连的话单元测试要桩五十多个方法。窄接口只要桩 1 个方法，代价是一个转发类（<see cref="MomoTransactionAdapter"/>）。
    ///
    /// 失败语义照抄 Momo：work 抛异常 → 回滚并原样上抛；<paramref name="commitWhen"/> 返回 false → 回滚但仍返回结果。
    /// </summary>
    internal interface ITransactionKernel
    {
        Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> work,
            Func<T, bool>? commitWhen,
            CancellationToken cancellationToken);
    }
}
