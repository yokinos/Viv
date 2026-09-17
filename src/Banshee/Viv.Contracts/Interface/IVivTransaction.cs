using System;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 窄事务句柄，业务自己划边界。
    ///
    /// <code>
    /// await using var tx = await _unitOfWork.BeginAsync();
    /// await _orderRepo.InsertAsync(order);
    /// await tx.CommitAsync();     // 不写这行 → 离开作用域时自动回滚
    /// </code>
    ///
    /// 嵌套：只有最外层真正开事务，嵌套拿到的是子句柄。子句柄提交是空操作（等最外层）；
    /// 未提交就释放、显式回滚、或抛异常，都会把整个作用域标记为 rollback-only（粘性），
    /// 此后最外层提交也只回滚。没有保存点，内层回滚会拖垮整个事务。
    /// </summary>
    public interface IVivTransaction : IAsyncDisposable, IDisposable
    {
        /// <summary>提交。嵌套子句柄上调用是空操作（等最外层）。幂等。</summary>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>回滚。会把整个作用域标记为 rollback-only。幂等。</summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
