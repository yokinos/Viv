using Microsoft.AspNetCore.Http;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// SSE 入口：控制器只调一次 <see cref="RunVivSseAsync(HttpResponse, Func{IVivSseSession, CancellationToken, Task}, string?, VivSseOptions?, CancellationToken)"/>，
    /// 帧怎么写、心跳怎么发、断了怎么收场全部交给这里。
    /// </summary>
    public static class VivSseExtensions
    {
        /// <summary>
        /// 把响应切换为 SSE 模式（Content-Type、禁用缓存与反代缓冲）
        /// </summary>
        public static void UseVivSse(this HttpResponse response)
        {
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers.CacheControl = "no-cache, no-store, no-transform";
            response.Headers["X-Accel-Buffering"] = "no";

            // Connection 是逐跳头，HTTP/2 里不允许出现
            if (string.Equals(response.HttpContext.Request.Protocol, "HTTP/1.1", StringComparison.OrdinalIgnoreCase))
            {
                response.Headers.Connection = "keep-alive";
            }
        }

        /// <summary>
        /// 开一条 SSE 流并跑完整个会话生命周期。
        /// </summary>
        /// <param name="response">当前响应</param>
        /// <param name="handler">业务生产逻辑：往 session 里推事件，推完返回即可（框架自动补 done 帧）</param>
        /// <param name="traceId">链路追踪标识，写进每帧 data</param>
        /// <param name="options">会话参数，null 时按宿主环境取安全默认值</param>
        /// <param name="cancellationToken">请求取消令牌</param>
        public static async Task RunVivSseAsync(this HttpResponse response,
            Func<IVivSseSession, CancellationToken, Task> handler,
            string? traceId = null,
            VivSseOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(handler);

            var httpContext = response.HttpContext;

            // 已经断开就别开始：写头会直接抛
            if (httpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            options ??= VivSseOptions.CreateFor(httpContext);
            options.Validate();

            response.UseVivSse();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, httpContext.RequestAborted);
            var token = linked.Token;

            var writer = new VivSseWriter(response.Body, options.SerializerOptions);
            var session = new VivSseSession(traceId, options.BufferCapacity, token);

            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            // 两个 Task 在这里声明、在 try 里赋值：retry 必须在第一帧之前落线，
            // 所以生产者只能在 retry 写完之后才启动
            Task? heartbeat = null;
            Task? producer = null;

            try
            {
                if (options.RetryMilliseconds is { } retry && retry > 0)
                {
                    await writer.WriteRawAsync($"retry: {retry}\n\n", token).ConfigureAwait(false);
                }

                heartbeat = HeartbeatLoopAsync(writer, options, heartbeatCts.Token);
                producer = Task.Run(() => RunProducerAsync(handler, session, options), CancellationToken.None);

                await foreach (var frame in session.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await writer.WriteFrameAsync(frame, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端断开或请求中止
            }
            catch (IOException)
            {
                // 对端 RST / 管道已断
            }
            catch (ObjectDisposedException)
            {
                // 响应已释放
            }
            finally
            {
                await StopAsync(session, producer, heartbeatCts, heartbeat, options, writer).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 便捷重载：不需要拿取消令牌的生产逻辑
        /// </summary>
        public static Task RunVivSseAsync(this HttpResponse response,
            Func<IVivSseSession, Task> handler,
            string? traceId = null,
            VivSseOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return response.RunVivSseAsync((session, _) => handler(session), traceId, options, cancellationToken);
        }

        /// <summary>
        /// 便捷重载：直接消费一段文本流，每段一个 delta 帧，末尾补 done 帧。
        /// </summary>
        /// <param name="response">当前响应</param>
        /// <param name="deltas">增量文本流</param>
        /// <param name="traceId">链路追踪标识</param>
        /// <param name="summaryFactory">结束帧的汇总数据工厂</param>
        /// <param name="options">会话参数</param>
        /// <param name="cancellationToken">请求取消令牌</param>
        public static Task RunVivSseAsync(this HttpResponse response,
            IAsyncEnumerable<string> deltas,
            string? traceId = null,
            Func<object?>? summaryFactory = null,
            VivSseOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(deltas);

            return response.RunVivSseAsync(async (session, token) =>
            {
                await foreach (var delta in deltas.WithCancellation(token).ConfigureAwait(false))
                {
                    if (!await session.SendDeltaAsync(delta).ConfigureAwait(false))
                    {
                        // 会话已关，停止拉取，避免白跑下游
                        break;
                    }
                }

                if (summaryFactory is not null)
                {
                    await session.CompleteAsync(summaryFactory()).ConfigureAwait(false);
                }
            }, traceId, options, cancellationToken);
        }

        private static async Task RunProducerAsync(Func<IVivSseSession, CancellationToken, Task> handler,
            VivSseSession session, VivSseOptions options)
        {
            try
            {
                await handler(session, session.CancellationToken).ConfigureAwait(false);
                await session.CompleteAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 客户端断开 / 响应中止：链路已经没了，终帧写不出去，直接收场
                session.Abort();
            }
            catch (Exception ex)
            {
                var message = options.ErrorMessageFactory?.Invoke(ex);
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = options.RedactErrorDetails
                        ? "服务端处理失败，请携带 traceId 反馈"
                        : ex.Message;
                }

                await session.FailAsync(message).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 空闲时补一行注释心跳。EventSource 会忽略注释，但字节到了对端就能顶住反代的空闲超时。
        /// </summary>
        private static async Task HeartbeatLoopAsync(VivSseWriter writer, VivSseOptions options, CancellationToken cancellationToken)
        {
            if (!options.HeartbeatEnabled)
            {
                return;
            }

            var interval = options.HeartbeatInterval;
            var intervalMs = (long)interval.TotalMilliseconds;

            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    // 刚写过业务帧就不必再补心跳，省掉无意义的字节
                    if (writer.IdleMilliseconds < intervalMs)
                    {
                        continue;
                    }

                    await writer.WriteCommentAsync("hb", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch (ObjectDisposedException)
            {
                // 正常退出
            }
            catch (IOException)
            {
                // 对端已断，写循环那边会感知并收场
            }
        }

        private static async Task StopAsync(VivSseSession session, Task? producer,
            CancellationTokenSource heartbeatCts, Task? heartbeat, VivSseOptions options, VivSseWriter writer)
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);

            if (heartbeat is not null)
            {
                await SwallowAsync(heartbeat).ConfigureAwait(false);
            }

            // 置关闭位 → 取消业务侧令牌 → 生产者应从 await 里抛出来
            session.MarkClosed();

            if (producer is not null)
            {
                try
                {
                    await Task.WhenAny(producer, Task.Delay(options.ShutdownGracePeriod)).ConfigureAwait(false);
                }
                catch
                {
                    // 生产者的异常不往外抛：响应已经结束，抛出去只会污染日志
                }
            }

            writer.Dispose();
            session.Dispose();
        }

        private static async Task SwallowAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // 收尾阶段的异常一律吞掉
            }
        }
    }
}
