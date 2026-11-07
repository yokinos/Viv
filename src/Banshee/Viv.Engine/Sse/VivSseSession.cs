using System.Threading.Channels;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// 有界 Channel 支撑的会话实现：生产者 ↔ 写循环之间只有这一条队列，
    /// 队列满就反压生产者，客户端慢不会把服务端内存吃穿。
    /// 同时持有「终态」与「已关闭」两个一次性闸门，保证 done/error/canceled 只有一个能上链路。
    /// </summary>
    internal sealed class VivSseSession : IVivSseSession, IDisposable
    {
        private readonly Channel<VivSseFrame> _channel;
        private readonly CancellationTokenSource _stop;
        private readonly CancellationTokenSource _token;
        private long _lastId;
        private int _terminal;
        private int _closed;
        private int _disposed;

        internal VivSseSession(string? traceId, int bufferCapacity, CancellationToken outerToken)
        {
            TraceId = traceId;

            _channel = Channel.CreateBounded<VivSseFrame>(new BoundedChannelOptions(bufferCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _stop = new CancellationTokenSource();
            _token = CancellationTokenSource.CreateLinkedTokenSource(outerToken, _stop.Token);
        }

        /// <inheritdoc />
        public string? TraceId { get; }

        /// <inheritdoc />
        public long LastEventId => Interlocked.Read(ref _lastId);

        /// <inheritdoc />
        public bool IsOpen => Volatile.Read(ref _terminal) == 0 && Volatile.Read(ref _closed) == 0;

        /// <inheritdoc />
        public CancellationToken CancellationToken => _token.Token;

        /// <summary>
        /// 写循环消费的帧队列（单读者）
        /// </summary>
        internal ChannelReader<VivSseFrame> Reader => _channel.Reader;

        /// <inheritdoc />
        public ValueTask<bool> SendAsync(object? payload, string eventName = VivSseEvents.Delta)
        {
            if (!IsOpen)
            {
                return new ValueTask<bool>(false);
            }

            var frame = VivSseFrame.Ok(payload, TraceId);
            frame.Event = eventName;
            frame.Id = Interlocked.Increment(ref _lastId);
            return EnqueueAsync(frame);
        }

        /// <inheritdoc />
        public ValueTask<bool> SendDeltaAsync(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return new ValueTask<bool>(true);
            }

            if (!IsOpen)
            {
                return new ValueTask<bool>(false);
            }

            var id = Interlocked.Increment(ref _lastId);
            var frame = VivSseFrame.Ok(new { delta, seq = id }, TraceId);
            frame.Event = VivSseEvents.Delta;
            frame.Id = id;
            return EnqueueAsync(frame);
        }

        /// <inheritdoc />
        public Task<bool> CompleteAsync(object? summary = null)
            => TerminateAsync(VivSseFrame.Done(summary, TraceId));

        /// <inheritdoc />
        public Task<bool> FailAsync(string message, int code = -1)
            => TerminateAsync(VivSseFrame.Fail(message, TraceId, code));

        /// <inheritdoc />
        public Task<bool> CancelAsync(string? reason = null)
            => TerminateAsync(VivSseFrame.Canceled(reason, TraceId));

        /// <summary>
        /// 不写终帧直接收场（链路已断，写也没人收）
        /// </summary>
        internal void Abort()
        {
            Interlocked.Exchange(ref _terminal, 1);
            _channel.Writer.TryComplete();
        }

        /// <summary>
        /// 写循环退出时调用：置关闭位、取消业务侧令牌，让生产者的 await 尽早抛出来
        /// </summary>
        internal void MarkClosed()
        {
            Interlocked.Exchange(ref _closed, 1);
            _channel.Writer.TryComplete();

            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已释放，无需再取消
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _channel.Writer.TryComplete();
            _token.Dispose();
            _stop.Dispose();
        }

        private async ValueTask<bool> EnqueueAsync(VivSseFrame frame)
        {
            try
            {
                await _channel.Writer.WriteAsync(frame, _token.Token).ConfigureAwait(false);
                return true;
            }
            catch (ChannelClosedException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task<bool> TerminateAsync(VivSseFrame frame)
        {
            // 第一个到达终态的赢，后面的直接返回 false
            if (Interlocked.Exchange(ref _terminal, 1) != 0)
            {
                return false;
            }

            frame.Id = Interlocked.Increment(ref _lastId);

            try
            {
                await _channel.Writer.WriteAsync(frame, _token.Token).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                // 队列已关，终帧丢掉；流关闭本身就是结束信号
            }
            catch (OperationCanceledException)
            {
                // 客户端已断开，同样无需再写
            }
            finally
            {
                _channel.Writer.TryComplete();
            }

            return true;
        }
    }
}
