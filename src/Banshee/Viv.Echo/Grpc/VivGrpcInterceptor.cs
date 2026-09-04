using Grpc.Core;
using Grpc.Core.Interceptors;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Delusion;

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
            return continuation(request, WithVivHeaders(context));
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(request, WithVivHeaders(context));
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(WithVivHeaders(context));
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            return continuation(WithVivHeaders(context));
        }

        /// <summary>
        /// 注入 x-viv-* 契约头（含 holder-id），有 InternalToken 时再签 x-request-token。
        /// gRPC metadata 键必须小写写盘。
        /// </summary>
        private ClientInterceptorContext<TRequest, TResponse> WithVivHeaders<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context)
            where TRequest : class
            where TResponse : class
        {
            var headers = context.Options.Headers ?? [];
            AddVivHeaders(headers);

            var newOptions = context.Options.WithHeaders(headers);
            return new ClientInterceptorContext<TRequest, TResponse>(
                context.Method, context.Host, newOptions);
        }

        private void AddVivHeaders(Metadata headers)
        {
            var tokenOptions = VivConfigRegistry.Get<VivInternalTokenOptions>();
            var serviceName = tokenOptions?.ServiceName ?? "";

            AddIfNotExist(headers, VivHeaderContract.AppId.ToLowerInvariant(), _vivContext.AppId.ToString());
            AddIfNotExist(headers, VivHeaderContract.SubjectId.ToLowerInvariant(), _vivContext.SubjectId.ToString());
            AddIfNotExist(headers, VivHeaderContract.UserId.ToLowerInvariant(), _vivContext.UserId.ToString());
            AddIfNotExist(headers, VivHeaderContract.ServiceName.ToLowerInvariant(), serviceName);
            AddIfNotExist(headers, VivHeaderContract.HolderId.ToLowerInvariant(), LockHolderContext.CurrentHolderId);

            var secret = tokenOptions?.InternalToken;
            if (string.IsNullOrWhiteSpace(secret)
                || headers.Get(VivHeaderContract.InnerRequestToken) != null)
            {
                return;
            }

            var token = VivRequestToken.Sign(
                headers.Get(VivHeaderContract.AppId)?.Value ?? "",
                headers.Get(VivHeaderContract.SubjectId)?.Value ?? "",
                headers.Get(VivHeaderContract.UserId)?.Value ?? "",
                headers.Get(VivHeaderContract.ServiceName)?.Value ?? "",
                headers.Get(VivHeaderContract.HolderId)?.Value ?? "",
                secret);
            headers.Add(VivHeaderContract.InnerRequestToken.ToLowerInvariant(), token);
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
