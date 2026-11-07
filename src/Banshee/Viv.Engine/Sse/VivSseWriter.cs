using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// 帧写入器。写循环与心跳是两条并发路径，ASP.NET Core 不允许并发写响应体，
    /// 所以所有输出都从这里过一道信号量；同时记录最后写入时刻，供心跳判断是否真的空闲。
    /// </summary>
    internal sealed class VivSseWriter
    {
        private readonly Stream _body;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly JsonSerializerOptions _json;
        private long _lastWriteTicks;

        internal VivSseWriter(Stream body, JsonSerializerOptions json)
        {
            _body = body;
            _json = json;
            _lastWriteTicks = Environment.TickCount64;
        }

        /// <summary>
        /// 距上次写出的毫秒数
        /// </summary>
        internal long IdleMilliseconds => Environment.TickCount64 - Interlocked.Read(ref _lastWriteTicks);

        /// <summary>
        /// 按 SSE 线格式写一帧：id / event / data(多行拆段) / 空行
        /// </summary>
        internal async Task WriteFrameAsync(VivSseFrame frame, CancellationToken cancellationToken)
        {
            var builder = new StringBuilder(256);

            if (frame.Id > 0)
            {
                builder.Append("id: ").Append(frame.Id.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            builder.Append("event: ").Append(frame.Event).Append('\n');

            var payload = JsonSerializer.Serialize(frame, _json);
            foreach (var line in payload.Split('\n'))
            {
                builder.Append("data: ").Append(line.TrimEnd('\r')).Append('\n');
            }

            builder.Append('\n');

            await WriteRawAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 写注释行（<c>: xxx</c>）。EventSource 会忽略它，但字节确实到了对端，能顶住反代空闲超时。
        /// </summary>
        internal Task WriteCommentAsync(string comment, CancellationToken cancellationToken)
            => WriteRawAsync($": {comment}\n\n", cancellationToken);

        /// <summary>
        /// 写裸文本（retry 等控制字段）
        /// </summary>
        internal async Task WriteRawAsync(string text, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(text);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _body.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await _body.FlushAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref _lastWriteTicks, Environment.TickCount64);
            }
            finally
            {
                _gate.Release();
            }
        }

        internal void Dispose() => _gate.Dispose();
    }
}
