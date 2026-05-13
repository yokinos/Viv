using Grpc.Core;
using Grpc.Core.Interceptors;
using Viv.Contracts.Interface;

namespace Viv.Echo.Grpc
{
    public class VivGrpcInterceptor : Interceptor
    {
        private readonly IVivContext _vivContext;

        public VivGrpcInterceptor(IVivContext vivContext)
        {
            _vivContext = vivContext;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var headers = context.Options.Headers ?? new Metadata();
            AddVivHeaders(headers);

            var newOptions = context.Options.WithHeaders(headers);
            var newContext = new ClientInterceptorContext<TRequest, TResponse>(
                context.Method, context.Host, newOptions);

            return continuation(request, newContext);
        }

        private void AddVivHeaders(Metadata headers)
        {
            AddIfNotExist(headers, "Viv-AppId", _vivContext.AppId.ToString());
            AddIfNotExist(headers, "Viv-TenantId", _vivContext.TenantId.ToString());
            AddIfNotExist(headers, "Viv-UserId", _vivContext.UserId.ToString());
        }

        private static void AddIfNotExist(Metadata headers, string key, string value)
        {
            if (headers.Get(key) == null)
            {
                headers.Add(key, value);
            }
        }
    }
}
