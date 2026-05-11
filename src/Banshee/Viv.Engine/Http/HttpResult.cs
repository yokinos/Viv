using System.Net;

namespace Viv.Engine.Http
{
    public class HttpResult
    {
        public bool IsSuccess { get; set; }

        public HttpStatusCode StatusCode { get; set; }

        public string? Message { get; set; }

        public string? ResponseMessage { get; set; }

        public long ElapsedMilliseconds { get; set; }
    }

    public class HttpResult<T> : HttpResult
    {
        public T? Response { get; set; }
    }
}
