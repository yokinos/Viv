using Microsoft.AspNetCore.SignalR;

namespace Viv.Kinna.Connections
{
    /// <summary>
    /// 按客户端串行化「查旧连接 → 登记新连接」这一段。
    ///
    /// 没有它的时候，同一客户端的两条连接并发注册会互相看不见对方：
    /// 两边都在对方登记之前查表，于是都拿到空结果、都成功 <c>Add</c>。
    /// 之后各自广播的顶号指令又会被本副本的订阅端按 <c>Origin == 自己</c> 跳过，
    /// 结果是同一 <c>AppId</c> 在同一副本上留下两条连接——重复收消息、在线态重复计数，
    /// 和 <see cref="Options.KinnaOptions.SingleConnectionPerApp"/> 的语义不符。
    ///
    /// 用固定条带而不是「每客户端一把锁」：后者要么永远不回收（连接表里每来过一个客户端
    /// 就留一把锁），要么得加引用计数，复杂度远超收益。条带冲突只是让两个无关客户端
    /// 偶发排队，而这段临界区只有两次字典查询加一次写入。
    /// </summary>
    public sealed class KinnaRegistrationGate<THub> where THub : Hub
    {
        /// <summary>
        /// 条带数。取 2 的幂，取模用位与；64 条对单机十万级连接足够，
        /// 真正会冲突的只是同一瞬间注册的那批。
        /// </summary>
        private const int StripeCount = 64;

        private readonly SemaphoreSlim[] _stripes;

        public KinnaRegistrationGate()
        {
            _stripes = new SemaphoreSlim[StripeCount];

            for (var i = 0; i < _stripes.Length; i++)
            {
                _stripes[i] = new SemaphoreSlim(1, 1);
            }
        }

        /// <summary>等这一客户端所在的条带。必须和 <see cref="Release"/> 配对，放在 finally 里。</summary>
        internal Task WaitAsync(KinnaConnectionKey key, CancellationToken cancellationToken)
            => Stripe(key).WaitAsync(cancellationToken);

        internal void Release(KinnaConnectionKey key) => Stripe(key).Release();

        private SemaphoreSlim Stripe(KinnaConnectionKey key)
        {
            // HashCode.Combine 会和进程级随机种子混在一起，这里要的是稳定映射，自己算
            var hash = key.AppId * 397L ^ key.SubjectId * 31L ^ key.UserId;
            return _stripes[(int)((ulong)hash & (StripeCount - 1))];
        }
    }
}
