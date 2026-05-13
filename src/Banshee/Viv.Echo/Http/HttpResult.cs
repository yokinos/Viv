using System.Net;

namespace Viv.Echo.Http
{
    public class HttpResult<T>
    {
        public bool IsSuccess { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public string? Message { get; set; }

        public string? ResponseJson { get; set; }

        public long ElapsedTime { get; set; }

        public T? Response { get; set; }
    }
}
