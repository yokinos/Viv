using System;
using System.Threading;
using System.Threading.Tasks;
using Viv.Momo;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 把 <see cref="ITransactionKernel"/> 接到 <c>IMomoDbContext.ExecuteInTransactionAsync</c> 上，纯转发零逻辑。
    ///
    /// 工作单元建在 Momo 的委托式事务之上：BEGIN/COMMIT 由 EF 执行策略拥有，
    /// 将来换内核，换这个类即可。
    ///
    /// 生命周期必须与 <c>IMomoDbContext</c> 对齐（都是 Scoped）：Momo 的事务状态挂在它自己的
    /// <c>_transaction</c> 字段上跟着实例走，适配器若被解析到别的实例（尤其是根作用域），
    /// 开的和提交的会是两个不同的事务。
    /// </summary>
    internal sealed class MomoTransactionAdapter : ITransactionKernel
    {
        private readonly IMomoDbContext _db;

        public MomoTransactionAdapter(IMomoDbContext db)
        {
            _db = db;
        }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, Func<T, bool>? commitWhen, CancellationToken cancellationToken)
            => commitWhen is null
                ? _db.ExecuteInTransactionAsync(work, cancellationToken)
                : _db.ExecuteInTransactionAsync(work, commitWhen, cancellationToken);
    }
}
