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
                TaskCompletionSource<bool>? completion)
                => new(null, status, description, completion);
        }

        private readonly string _hub;
        private readonly WebSocket _socket;
        private readonly Channel<OutboundFrame> _outbound;
        private readonly CancellationTokenSource _lifetime;
        private readonly TimeProvider _time;
        private readonly TimeSpan _closeTimeout;
        private Task _sender = Task.CompletedTask;
        private int _aborted;
        private int _closing;
        private int _disposed;

        internal KinnaWebSocketConnection(
            string connectionId,
            string hub,
            WebSocket socket,
            int queueCapacity,
            CancellationToken requestAborted,
            TimeProvider? timeProvider = null,
            KinnaWebSocketTimeouts? timeouts = null)
        {
            ConnectionId = connectionId;
            _hub = hub;
            _socket = socket;
            _time = timeProvider ?? TimeProvider.System;
            _closeTimeout = (timeouts ?? KinnaWebSocketTimeouts.Default).CloseFrame;
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

        /// <summary>
        /// 还能不能往这条连接上写。
        ///
        /// 除 <c>_aborted</c> 和 socket 状态外还要看写循环是否还活着：<c>SendAsync</c> 抛
        /// <c>WebSocketException</c>（对端 RST）时写循环直接退出，而 <c>_aborted</c> 没置位、
        /// <c>socket.State</c> 往往还是 <c>Open</c>。这种连接入队只会永远没人消费：
        /// 数据帧白占内存，关闭帧要等满 <see cref="KinnaWebSocketTimeouts.CloseFrame"/> 才判失败。
        /// </summary>
        internal bool IsOpen => Volatile.Read(ref _aborted) == 0
                                && !Volatile.Read(ref _sender).IsCompleted
                                && _socket.State == WebSocketState.Open;

        /// <summary>
        /// 是否已进入关闭流程（关闭帧已入队）。之后的数据帧不再接受：
        /// 收到对端 Close 后协议本身也不允许再发数据帧。
        /// </summary>
        internal bool IsClosing => Volatile.Read(ref _closing) != 0;

        /// <summary>
        /// 标记进入关闭流程。收到对端 Close、判定失效、帧过大时由端点调用：
        /// 让串行队列里跑完的回帧走「静默丢弃」，而不是「入队失败 → Unbind → Abort」——
        /// 后者会抢在客户端读到关闭帧之前把握手掐成 1006。
        /// </summary>
        internal void MarkClosing() => Interlocked.Exchange(ref _closing, 1);

        /// <summary>
        /// 还能不能写关闭帧。比 <see cref="IsOpen"/> 宽一档：客户端先发了关闭帧时
        /// 状态是 <c>CloseReceived</c>，此时我们要回的那一帧仍然是合法的最后一次写；
        /// 但已经 <c>CloseSent</c> 就不能再发，会直接抛。
        ///
        /// 还要看写循环是否还活着：requestAborted 一取消，<c>_lifetime</c> 就取消、写循环退出，
        /// 队列里的关闭指令再没人消费。这时入队只能等满 CloseFrame 超时——
        /// 客户端刷新、断网是最高频的断开路径，每条都白等 5 秒没有意义。
        /// 写循环因发送异常退出（<see cref="IsOpen"/> 里那条）同理，那条路径上一版漏掉了。
        /// </summary>
        private bool CanWriteClose
        {
            get
            {
                if (Volatile.Read(ref _aborted) != 0 || Volatile.Read(ref _disposed) != 0)
                {
                    return false;
                }

                return !_lifetime.IsCancellationRequested
                       && !Volatile.Read(ref _sender).IsCompleted
                       && _socket.State is WebSocketState.Open or WebSocketState.CloseReceived;
            }
        }

        private bool TryEnqueueClose(
            WebSocketCloseStatus status,
            string? description,
            TaskCompletionSource<bool>? completion)
        {
            if (!CanWriteClose || !_outbound.Writer.TryWrite(OutboundFrame.Close(status, description, completion)))
            {
                return false;
            }

            // 入队成功就进入关闭流程：关闭帧是这条连接上的最后一次写，之后的数据帧一律丢弃。
            // 这个标记同时挡住「排空串行队列时跑完的 Ack 回帧」——它们入不了队，
            // 若不挡住，Registry.Enqueue 会把失败当成慢客户端，Unbind→Abort 掉正在握手的连接
            MarkClosing();
            return true;
        }

        /// <summary>
        /// 启动写循环。任务由连接自己保管：断开时要等它排空，才不至于把最后几帧丢掉。
        /// </summary>
        internal void Start() => _sender = RunSenderAsync();

        /// <summary>
        /// 入队一帧，返回 false 表示队列已满或已进入关闭流程（调用方应当区分对待）
        /// </summary>
        internal bool TryEnqueue(byte[] frame)
            => IsOpen && !IsClosing && _outbound.Writer.TryWrite(OutboundFrame.Text(frame));

        /// <summary>
        /// 写循环的完成状态：队列排空或被取消后完成。停机时要靠它有界等待关闭帧真正写出去。
        /// </summary>
        internal Task SenderCompletion => _sender;

        /// <summary>
        /// 只把关闭帧排进队列，不等它写出去。
        ///
        /// 软踢（<c>KinnaWebSocketRegistry.Terminate</c>）和停机（<c>AbortAll</c>）用这条：
        /// 那边自己已经有宽限兜底，再 <c>await</c> 一遍只会让「被踢」在指标上多记一条
        /// <c>close_timeout</c>——宽限（<see cref="KinnaWebSocketTimeouts.TerminateGrace"/> /
        /// <see cref="KinnaWebSocketTimeouts.ShutdownGrace"/>）比 <see cref="KinnaWebSocketTimeouts.CloseFrame"/> 短，
        /// 等的那个必然超时。
        /// </summary>
        internal bool EnqueueClose(WebSocketCloseStatus status, string? description)
            => TryEnqueueClose(status, description, null);

        /// <summary>
        /// 排空在途帧之后发一个关闭帧。返回的任务在关闭帧真正写出后完成，
        /// 结果为 false 表示这一帧没发出去（队列满、连接已关、已经发过关闭帧，或等待超时）——
        /// 这种情况调用方只能硬断，再等一个发不出去的帧没有意义。
        ///
        /// 等待有上限：写循环未必还活着（请求中止会先取消它，见 <see cref="RunSenderAsync"/>），
        /// 那种情况下队列里的关闭指令永远不会被处理，调用方就卡死在一条已经没救的连接上，
        /// 连着 socket 和请求作用域一起泄漏到进程结束。
        /// </summary>
        internal async Task<bool> CloseOutputAsync(WebSocketCloseStatus status, string? description)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!TryEnqueueClose(status, description, completion))
            {
                return false;
            }

            try
            {
                return await completion.Task.WaitAsync(_closeTimeout, _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // 关闭帧还留在队列里：写循环要是后来活了，它仍会写出去，这条只代表「没等到」
                KinnaMetrics.RecordWebSocketClosed(_hub, KinnaWebSocketCloseReasons.CloseTimeout);
                return false;
            }
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
                            // 用 _lifetime 而不是 None：Abort 时要能打断卡在发送上的关闭帧，
                            // 否则对端不读时写循环永远退不出去，FinishAsync 的宽限也收不回它
                            await _socket
                                .CloseOutputAsync(item.CloseStatus, item.CloseDescription, _lifetime.Token)
                                .ConfigureAwait(false);

                            item.Closed?.TrySetResult(true);
                        }
                        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException
                                                       or OperationCanceledException or ArgumentException)
                        {
                            // 对端已经走了：关闭帧发不出去不算错误，连接本来就要销毁。
                            // ArgumentException 是状态码/描述不被接受（描述超过 123 字节），同样不该炸掉写循环
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

            // 写循环退出即说明队列排空；客户端卡住时不能无限等，到点由 Abort 取消写循环。
            // 定时走 TimeProvider：这条路径上的宽限期是要被测的，真等 1 秒的用例没有意义
            await Task.WhenAny(_sender, Task.Delay(grace, _time)).ConfigureAwait(false);

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
