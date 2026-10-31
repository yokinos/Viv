using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// SSE 写入扩展：把帧信封序列化成 data 帧并立即 flush。
    /// 控制器只调一次 <see cref="WriteSseStreamAsync"/>。
    /// </summary>
    public static class VivSseExtensions
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// 把响应切换为 SSE 模式（设置 Content-Type、禁用缓存与代理缓冲）
        /// </summary>
        public static void UseVivSse(this HttpResponse response)
        {
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";
            response.Headers["X-Accel-Buffering"] = "no";
        }

        /// <summary>
        /// 写一帧信封并立即 flush
        /// </summary>
        public static async Task WriteSseFrameAsync(this HttpResponse response, VivSseFrame frame, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(frame, JsonOptions);
            await response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// 把增量文本流一次写完：每段一个 { delta, seq } 帧，末尾补一个结束帧；
        /// 客户端断开按正常结束处理，异常则写一个错误帧
        /// </summary>
        public static async Task WriteSseStreamAsync(this HttpResponse response,
            IAsyncEnumerable<string> deltas,
            string? traceId = null,
            Func<object?>? summaryFactory = null,
            CancellationToken cancellationToken = default)
        {
            response.UseVivSse();

            var seq = 0;

            try
            {
                await foreach (var delta in deltas.WithCancellation(cancellationToken))
                {
                    seq++;
                    await response.WriteSseFrameAsync(VivSseFrame.Ok(new { delta, seq }, traceId), cancellationToken);
                }

                await response.WriteSseFrameAsync(VivSseFrame.Done(summaryFactory?.Invoke(), traceId), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 客户端断开，属正常结束，不再写
            }
            catch (Exception ex)
            {
                await response.WriteSseFrameAsync(VivSseFrame.Fail(ex.Message, traceId), CancellationToken.None);
            }
        }
    }
}
