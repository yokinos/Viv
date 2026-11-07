using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Viv.Engine.Sse
{
    /// <summary>
    /// SSE 会话参数。<see cref="Default"/> 是安全默认值（错误详情不外泄），
    /// 需要按环境降级时用 <see cref="CreateFor"/> 或直接改属性。
    /// </summary>
    public sealed class VivSseOptions
    {
        /// <summary>
        /// 安全默认配置（心跳 15s、缓冲 256 帧、错误详情脱敏）
        /// </summary>
        public static VivSseOptions Default { get; } = new();

        private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(15);
        private int _bufferCapacity = 256;

        /// <summary>
        /// 空闲心跳间隔，写一行 <c>: hb</c> 注释行（不触发客户端 message 事件、不占 id）。
        /// 小于等于零表示关闭心跳。默认 15 秒，短于常见反代的 60 秒空闲超时。
        /// </summary>
        public TimeSpan HeartbeatInterval
        {
            get => _heartbeatInterval;
            set => _heartbeatInterval = value;
        }

        /// <summary>
        /// 是否开启心跳
        /// </summary>
        public bool HeartbeatEnabled => _heartbeatInterval > TimeSpan.Zero;

        /// <summary>
        /// 重连建议毫秒数，写成 <c>retry:</c> 字段；null 表示不下发。默认 3000。
        /// </summary>
        public int? RetryMilliseconds { get; set; } = 3000;

        /// <summary>
        /// 背压缓冲帧数：写满后生产者 await 等待，客户端慢不会把服务端内存撑爆。默认 256。
        /// </summary>
        public int BufferCapacity
        {
            get => _bufferCapacity;
            set => _bufferCapacity = value;
        }

        /// <summary>
        /// 是否隐藏异常原文（生产默认 true）。开启后错误帧只给固定文案 + traceId，
        /// 原文走日志；关闭时直接透出 <see cref="Exception.Message"/>（仅开发环境该关）。
        /// </summary>
        public bool RedactErrorDetails { get; set; } = true;

        /// <summary>
        /// 自定义错误文案：拿到异常、返回可以给客户端看的字符串。返回 null 则按 <see cref="RedactErrorDetails"/> 处理。
        /// </summary>
        public Func<Exception, string?>? ErrorMessageFactory { get; set; }

        /// <summary>
        /// 客户端断开后等待业务收尾的宽限期，超时直接放手（不拖住请求）。默认 5 秒。
        /// </summary>
        public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// data 段的序列化参数。默认 Web 驼峰 + 不转义非 ASCII：
        /// 中文增量按 UTF-8 直出（3 字节/字），而不是转成 6 字节的 \uXXXX。
        /// SSE 的 data 只被 EventSource 当文本收，不进 HTML，放松转义是安全的；
        /// 若前端会把内容直接拼进 innerHTML，请自行换回默认 Encoder。
        /// </summary>
        public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 按宿主环境生成配置：只有 Development 才透出异常原文，其余一律脱敏。
        /// </summary>
        public static VivSseOptions CreateFor(IHostEnvironment? environment)
            => new() { RedactErrorDetails = environment is null || !environment.IsDevelopment() };

        /// <summary>
        /// 从请求服务容器里取宿主环境生成配置（取不到就按生产处理）
        /// </summary>
        public static VivSseOptions CreateFor(HttpContext context)
            => CreateFor(context.RequestServices.GetService<IHostEnvironment>());

        internal void Validate()
        {
            if (_bufferCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(BufferCapacity), _bufferCapacity, "背压缓冲至少 1 帧");
            }

            if (SerializerOptions is null)
            {
                throw new ArgumentNullException(nameof(SerializerOptions));
            }
        }
    }
}
