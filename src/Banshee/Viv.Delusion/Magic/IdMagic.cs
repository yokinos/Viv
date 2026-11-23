using System;
using System.Threading;
using Viv.Delusion.Snowflake;

namespace Viv.Delusion.Magic
{
    /// <summary>
    /// ID 生成门面 —— Viv 里拿唯一 ID 的统一入口。
    ///
    /// <para>
    /// 只有一条路径：走 <see cref="Default"/> 指向的框架默认生成器，机器号由 Engine 在装配期
    /// 从 <c>EnvOption.MachineId</c> 注入。业务代码<b>不需要也不应该</b>关心自己是哪台机器 ——
    /// 雪花本身全局唯一，按用途（锁持有者、请求追踪）另切一个机器号只会在同一毫秒制造
    /// 两个互不知情的序列，反而更容易撞。
    /// </para>
    ///
    /// <para>
    /// 为什么是静态门面而不是纯 DI：<c>VivCallContext</c> 是 <see cref="AsyncLocal{T}"/> 静态上下文，
    /// 消息信封的 ID 是属性初始化器 —— 这两处物理上拿不到容器，必须有个静态入口。
    /// 需要注入的地方请用 <see cref="IIdGenerator"/>，不要用本类。
    /// </para>
    /// </summary>
    public static class IdMagic
    {
        /// <summary>
        /// 未装配时的兜底机器号。
        ///
        /// <para>
        /// 0 与各宿主配置的 2~11 不冲突，但它<b>不是</b>安全的默认值：同一进程组内若有多个宿主
        /// 都忘了装配，它们会共用 0 并在同毫秒产出相同 ID。Engine 装配时必定覆盖成真实机器号，
        /// 这里的兜底只是为了让单测、工具类等没有 Engine 的场景不至于直接抛异常。
        /// </para>
        /// </summary>
        private const long FallbackMachineId = 0;

        private static IIdGenerator? _default;

        /// <summary>
        /// 框架默认 ID 生成器。未显式装配时按 <see cref="FallbackMachineId"/> 懒创建。
        /// </summary>
        public static IIdGenerator Default
        {
            get
            {
                // 读一次快照，装配完成后就不必再走 CAS
                var current = Volatile.Read(ref _default);
                if (current is not null) return current;

                // 并发首访时只能有一个实例胜出：两个机器号相同的生成器各跑各的序列，
                // 会在同一毫秒产出完全相同的 ID —— 本类要避免的正是这件事，不能在最底层重新引入
                var created = new DefaultIdGenerator(FallbackMachineId);
                return Interlocked.CompareExchange(ref _default, created, null) ?? created;
            }
            set => _default = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 是否已由装配方显式绑定默认生成器。装配期自检用：为 false 说明这个宿主没走 Engine。
        /// </summary>
        public static bool IsDefaultBound => Volatile.Read(ref _default) is not null;

        /// <summary>
        /// 绑定框架默认生成器。Engine 装配时调用一次，机器号取 <c>EnvOption.MachineId</c>。
        /// </summary>
        /// <param name="generator">生成器实例，通常同时注册为 <see cref="IIdGenerator"/> 单例</param>
        public static void SetDefault(IIdGenerator generator) => Default = generator;

        /// <summary>
        /// 生成全局唯一 ID。
        /// </summary>
        public static long NextId() => Default.NextId();

        /// <summary>
        /// 字符串格式
        /// </summary>
        /// <returns></returns>
        public static string NextIdString() => NextId().ToString();
    }
}
