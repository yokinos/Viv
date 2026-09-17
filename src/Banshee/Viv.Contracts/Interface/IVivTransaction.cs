using System;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// 窄事务句柄 —— 业务自己划边界，写多少包多少。
    ///
    /// 【用法：<c>await using</c>，忘提交即自动回滚】
    /// <code>
    /// await using var tx = await _unitOfWork.BeginAsync();
    /// await _orderRepo.InsertAsync(order);
    /// await _itemRepo.InsertBatchAsync(items);
    /// await tx.CommitAsync();     // 不写这行 → 离开作用域时自动回滚
    /// </code>
    ///
    /// 【嵌套语义 —— 不是 TransactionScope，没有保存点】
    /// - 只有**最外层**那次 <c>BeginAsync</c> 真正开事务；嵌套调用拿到的是<b>子句柄</b>，
    ///   不再穿透到数据库。
    /// - 子句柄调 <c>CommitAsync</c> 是<b>空操作</b>（不提交，等最外层）。
    /// - 子句柄未提交就释放、显式 <c>RollbackAsync</c>、或抛出异常 → 整个作用域被标记为
    ///   <b>rollback-only（粘性）</b>：此后最外层即使调 <c>CommitAsync</c> 也只回滚，并记 Warning。
    /// - ⚠️ <b>没有保存点</b> —— 内层回滚不会「只撤销内层的写」，它会拖垮整个事务。
    ///   要部分回滚请自己用窄事务划线，别指望嵌套。
    ///
    /// 【幂等】<c>CommitAsync</c> / <c>RollbackAsync</c> 可重复调用，只有第一次生效 ——
    /// <c>await using</c> 里显式提交后再释放不会重复动作。
    /// </summary>
    public interface IVivTransaction : IAsyncDisposable, IDisposable
    {
        /// <summary>提交。嵌套子句柄上调用是空操作（等最外层）。幂等。</summary>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>回滚。会把整个作用域标记为 rollback-only。幂等。</summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
