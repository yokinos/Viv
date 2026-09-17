using System.Threading;
using System.Threading.Tasks;
using Viv.Momo;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 把 <see cref="ITransactionKernel"/> 接到 Momo 现有的三个事务方法上 —— 纯转发，零逻辑。
    ///
    /// 【这是「Viv.Momo 一行不动」的支点】
    /// 工作单元的所有能力都建在 <c>IMomoDbContext</c> 已有的 Begin / Commit / Rollback 之上，
    /// 不改 Momo 的源码、接口、读路由，也不改它的任何行为。将来若要换内核，换这个类即可。
    ///
    /// ⚠️ <b>生命周期必须与 <c>IMomoDbContext</c> 对齐（都是 Scoped）</b> ——
    /// Momo 的事务状态挂在它自己的 <c>_transaction</c> 字段上，跟着实例走。
    /// 适配器若被解析到别的实例（尤其是根作用域），开的和提交的会是两个不同的事务。
    /// </summary>
    internal sealed class MomoTransactionAdapter : ITransactionKernel
    {
        private readonly IMomoDbContext _db;

        public MomoTransactionAdapter(IMomoDbContext db)
        {
            _db = db;
        }

        public Task<bool> BeginAsync(CancellationToken cancellationToken = default)
            => _db.BeginTransactionAsync(cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default)
            => _db.CommitTransactionAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => _db.RollbackTransactionAsync(cancellationToken);
    }
}
