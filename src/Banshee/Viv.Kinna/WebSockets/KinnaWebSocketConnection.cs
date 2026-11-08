using System.Net.WebSockets;
using System.Threading.Channels;
using Viv.Kinna.Metrics;

namespace Viv.Kinna.WebSockets
{
    /// <summary>
    /// 一条原生 WebSocket 连接。发送走单读者队列 + 单写循环：
    /// WebSocket 不允许并发 SendAsync，而业务侧推送来自任意线程（事件投递、踢人），
    /// 所以入队和真正的写必须分开。
    ///
    /// 关闭帧也走这条队列（<see cref="CloseOutputAsync"/>）。以前端点在读循环线程上直接
    /// <c>socket.CloseOutputAsync</c>，会和写循环抢同一个 socket：关闭帧可能抢在
    /// 前面把「为什么关」的那一帧（失效的 Pong、ForceDisconnect）吃掉，客户端只看到断线。
    ///
    /// 队列有界，满了不算等待而是直接判死：慢客户端继续留着只会把内存吃掉，
    /// 断开后客户端重连、走 Resync 补拉，比拖垮服务端划算。
    /// </summary>
    internal sealed class KinnaWebSocketConnection : IDisposable
    {
        /// <summary>
        /// 队列里的一帧。Data 为空表示这是一条关闭指令——它必须是这条连接上的最后一次写，
        /// 写循环发完就退出。
        /// </summary>
        private readonly record struct OutboundFrame(
            byte[]? Data,
            WebSocketCloseStatus CloseStatus,
            string? CloseDescription,
            TaskCompletionSource<bool>? Closed)
        {
            internal static OutboundFrame Text(byte[] data)
                => new(data, default, null, null);

            internal static OutboundFrame Close(
                WebSocketCloseStatus status,
                string? description,
                TaskCompletionSource<bool> completion)
                => new(null, status, description, completion);
        }

        private readonly string _hub;
        private readonly WebSocket _socket;
        private readonly Channel<OutboundFrame> _outbound;
        private readonly CancellationTokenSource _lifetime;
        private Task _sender = Task.CompletedTask;
        private int _aborted;
        private int _disposed;

        internal KinnaWebSocketConnection(
            string connectionId,
            string hub,
            WebSocket socket,
            int queueCapacity,
            CancellationToken requestAborted)
        {
            ConnectionId = connectionId;
            _hub = hub;
            _socket = socket;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);

            _outbound = Channel.CreateBounded<OutboundFrame>(new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        internal string ConnectionId { get; }

        internal bool IsOpen => Volatile.Read(ref _aborted) == 0 && _socket.State == WebSocketState.Open;

        /// <summary>
        /// 还能不能写关闭帧。比 <see cref="IsOpen"/> 宽一档：客户端先发了关闭帧时
        /// 状态是 <c>CloseReceived</c>，此时我们要回的那一帧仍然是合法的最后一次写；
        /// 但已经 <c>CloseSent</c> 就不能再发，会直接抛。
        /// </summary>
        private bool CanWriteClose => Volatile.Read(ref _aborted) == 0
                                      && _socket.State is WebSocketState.Open or WebSocketState.CloseReceived;

        /// <summary>
        /// 启动写循环。任务由连接自己保管：断开时要等它排空，才不至于把最后几帧丢掉。
        /// </summary>
        internal void Start() => _sender = RunSenderAsync();

        /// <summary>
        /// 入队一帧，返回 false 表示队列已满（调用方应当断开这条连接）
        /// </summary>
        internal bool TryEnqueue(byte[] frame) => IsOpen && _outbound.Writer.TryWrite(OutboundFrame.Text(frame));

        /// <summary>
        /// 排空在途帧之后发一个关闭帧。返回的任务在关闭帧真正写出后完成，
        /// 结果为 false 表示关闭指令根本没排上队（队列满、连接已关或已经发过关闭帧）——
        /// 这种情况调用方只能硬断，再等一个发不出去的帧没有意义。
        /// </summary>
        internal Task<bool> CloseOutputAsync(WebSocketCloseStatus status, string? description)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!CanWriteClose || !_outbound.Writer.TryWrite(OutboundFrame.Close(status, description, completion)))
            {
                completion.TrySetResult(false);
            }

            return completion.Task;
        }

        /// <summary>
        /// 发送循环。队列关闭或连接断开时退出，异常不往外抛——读循环会感知到连接已死并收尾。
        /// </summary>
        internal async Task RunSenderAsync()
        {
            try
            {
                while (await _outbound.Reader.WaitToReadAsync(_lifetime.Token).ConfigureAwait(false))
                {
                    while (_outbound.Reader.TryRead(out var item))
                    {
                        if (item.Data is { } data)
                        {
                            await _socket
                                .SendAsync(data, WebSocketMessageType.Text, true, _lifetime.Token)
                                .ConfigureAwait(false);

                            continue;
                        }

                        // 关闭帧是最后一次写：先封队列，确保后面不会再塞进新帧插到它后面
                        _outbound.Writer.TryComplete();

                        try
                        {
                            await _socket
                                .CloseOutputAsync(item.CloseStatus, item.CloseDescription, CancellationToken.None)
                                .ConfigureAwait(false);

                            item.Closed?.TrySetResult(true);
                        }
                        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException
                                                       or OperationCanceledException)
                        {
                            // 对端已经走了：关闭帧发不出去不算错误，连接本来就要销毁
                            item.Closed?.TrySetResult(false);
                        }

                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (WebSocketException)
            {
                // 对端已断。写循环到此为止，读循环随后也会退出，但这条连接在指标上要留痕——
                // 否则「消息没发出去」在线上只能靠日志猜
                KinnaMetrics.RecordWebSocketClosed(_hub, KinnaWebSocketCloseReasons.SendFailed);
            }
            catch (ObjectDisposedException)
            {
                // 已释放，收尾路径里的正常情况，不计失败
            }
        }

        /// <summary>
        /// 软踢之后收尾：先关队列让写循环把已经在途的帧（ForceDisconnect 就是其中一帧）发完，
        /// 最多等 <paramref name="grace"/>，然后硬断。
        ///
        /// 只入队不断开是不行的：客户端不配合（冻结、忽略未知帧）时，连接表里已经没有它、
        /// 推送永远到不了，它自己却还以为连着——这种「连接正常但收不到消息」最难查。
        /// </summary>
        internal async Task FinishAsync(TimeSpan grace)
        {
            _outbound.Writer.TryComplete();

            // 写循环退出即说明队列排空；客户端卡住时不能无限等，到点由 Abort 取消写循环
            await Task.WhenAny(_sender, Task.Delay(grace)).ConfigureAwait(false);

            Abort();
            Dispose();
        }

        /// <summary>
        /// 放弃这条连接：取消写循环并硬断 socket。
        /// </summary>
        internal void Abort()
        {
            if (Interlocked.Exchange(ref _aborted, 1) != 0)
            {
                return;
            }

            _outbound.Writer.TryComplete();

            try
            {
                _socket.Abort();
            }
            catch (ObjectDisposedException)
            {
                // 已释放
            }

            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已释放
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _outbound.Writer.TryComplete();
            _lifetime.Dispose();
        }
    }
}
