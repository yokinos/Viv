using System.Net;

namespace Viv.Echo.Http
{
    public class HttpResult<T>
    {
        /// <summary>
        /// 请求是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Http状态码
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// 返回消息
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 响应内容（json格式）
        /// </summary>
        public string? ResponseJson { get; set; }

        /// <summary>
        /// 响应时间（毫秒）
        /// </summary>
        public long ElapsedTime { get; set; }

        /// <summary>
        /// 响应内容（对象格式）
        /// </summary>
        public T? Response { get; set; }
    }
}
