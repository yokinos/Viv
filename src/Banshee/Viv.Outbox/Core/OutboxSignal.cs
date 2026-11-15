using System.Threading.Channels;

namespace Viv.Outbox.Core
{
    /// <summary>
    /// 投递器的唤醒信号 —— 入队提交之后把"睡着的投递器"叫起来，让它立刻去认领，
    /// 而不是干等一个轮询间隔。
    ///
    /// 容量只有 1 且 <see cref="BoundedChannelFullMode.DropWrite"/>：这是<b>故意的合并语义</b>。
    /// 一秒内入队 100 条消息只该唤醒一次投递器（它一轮会把表排空），
    /// 每条消息各唤醒一次等于让投递器空转 99 次，每次都是一轮认领查询。
    /// 用 DropWrite 而不是 Wait：这里不关心"到底丢没丢"，丢掉的语义就是"已经有一次待处理的唤醒了"，
    /// 正是想要的效果。
    ///
    /// 单例注册，跨作用域共享。写入方是各业务作用域，读者只有投递器一个。
    /// </summary>
    internal sealed class OutboxSignal
    {
        /// <summary>
        /// 永远不会有信号的实例 —— 给"没有快路径"的场景（单元测试直建投递器）用。
        /// 有它之后投递器的等待只有一个分支：等到信号或等满兜底间隔，
        /// 不用为 null 再写一套 Task.Delay。
        /// </summary>
        internal static readonly OutboxSignal Never = new();

        private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            // Wait 模式下 TryWrite 满时返回 false，但这里 DropWrite 更省事：
            // 两种模式对"是否真的丢了一条"的判断不同，而本类型压根不需要判断
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        /// <summary>
        /// 叫醒投递器。可从任意线程调用，非阻塞；已有一次未消费的唤醒时静默合并。
        /// </summary>
        public void Notify() => _wake.Writer.TryWrite(0);

        /// <summary>
        /// 等待"有活干了"的信号：期间被 <see cref="Notify"/> 叫醒就立刻返回，
        /// 否则最多等到 <paramref name="timeout"/> 到点（兜底轮询）。
        ///
        /// 超时用带 <see cref="TimeProvider"/> 的 CTS 而不是 <c>Task.WhenAny(Task.Delay(...), read)</c> —
        /// 后者在超时那次会留下一个悬空的 <c>ReadAsync</c>，下一次 Notify 会被它吃掉，
        /// 表现为"信号偶尔丢一次"，比慢更难查。
        /// </summary>
        /// <returns>true = 被信号唤醒；false = 超时（该走兜底轮询了）。取消时抛 <see cref="OperationCanceledException"/>。</returns>
        public async Task<bool> WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
        {
            using var timeoutCts = new CancellationTokenSource(timeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await _wake.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 超时：把取消原样传出去，别把"停机"吃成"超时"
                return false;
            }
        }
    }
}
