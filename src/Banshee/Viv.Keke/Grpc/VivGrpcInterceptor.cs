using Grpc.Core;
using Grpc.Core.Interceptors;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Keke.Identity;

namespace Viv.Keke.Grpc
{
    /// <summary>
    /// gRPC 客户端身份头拦截器。头的构造与签名在 <see cref="VivIdentityHeaders"/> 里（传输无关），
    /// 这里只负责把值写进 gRPC <see cref="Metadata"/>，并按“已存在就不覆盖”的语义补齐。
    /// </summary>
    public class VivGrpcInterceptor : Interceptor
    {
        private readonly IVivContext _vivContext;

        private readonly VivInternalTokenOptions _tokenOptions;

        public VivGrpcInterceptor(IVivContext vivContext, VivInternalTokenOptions tokenOptions)
        {
            _vivContext = vivContext;
            _tokenOptions = tokenOptions;
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
            var values = VivIdentityHeaders.Build(_vivContext, _tokenOptions);

            AddIfNotExist(headers, VivIdentityHeaders.Key(VivHeaderContract.AppId), values.AppId);
            AddIfNotExist(headers, VivIdentityHeaders.Key(VivHeaderContract.SubjectId), values.SubjectId);
            AddIfNotExist(headers, VivIdentityHeaders.Key(VivHeaderContract.UserId), values.UserId);
            AddIfNotExist(headers, VivIdentityHeaders.Key(VivHeaderContract.ServiceName), values.ServiceName);
            AddIfNotExist(headers, VivIdentityHeaders.Key(VivHeaderContract.HolderId), values.HolderId);

            if (!string.IsNullOrWhiteSpace(values.RequestToken))
            {
                AddIfNotExist(headers, VivIdentityHeaders.Key(VivHeaderContract.InnerRequestToken), values.RequestToken);
            }
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
