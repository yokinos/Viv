using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;
using Viv.Delusion;

namespace Viv.Echo.Grpc
{
    /// <summary>
    /// gRPC 服务端租户上下文恢复拦截器。
    ///
    /// VivContextMiddleware 只对 /api 前缀水合上下文，gRPC 路径必须由此拦截器从 x-viv-* 头恢复。
    /// 配置了 InternalToken 时必须验 x-request-token；未配置密钥则不信任裸头（不灌上下文）。
    /// </summary>
    public class VivGrpcServerInterceptor : Interceptor
    {
        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            using var _ = BeginVivContext(context);
            return continuation(request, context);
        }

        public override Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            using var _ = BeginVivContext(context);
            return continuation(request, responseStream, context);
        }

        public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            ServerCallContext context,
            ClientStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            using var _ = BeginVivContext(context);
            return continuation(requestStream, context);
        }

        public override Task DuplexStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            DuplexStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            using var _ = BeginVivContext(context);
            return continuation(requestStream, responseStream, context);
        }

        private static IDisposable BeginVivContext(ServerCallContext context)
        {
            var vivContext = context.GetHttpContext().RequestServices.GetRequiredService<IVivContext>();
            var content = TryBuildContext(context.RequestHeaders);
            if (content != null)
            {
                vivContext.SetSnapshot(content);
            }

            return new VivContextScope(vivContext);
        }

        /// <summary>
        /// AppId + UserId 必须为正才认头；SubjectId 可选。有 InternalToken 时必须 HMAC 通过。
        /// </summary>
        private static VivContextContent? TryBuildContext(Metadata headers)
        {
            var secret = VivConfigRegistry.Get<VivInternalTokenOptions>()?.InternalToken;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            if (!TryReadPositiveLong(headers, VivHeaderContract.AppId, out var appId))
            {
                return null;
            }

            if (!TryReadPositiveLong(headers, VivHeaderContract.UserId, out var userId))
            {
                return null;
            }

            TryReadPositiveLong(headers, VivHeaderContract.SubjectId, out var subjectId);
            var serviceName = headers.Get(VivHeaderContract.ServiceName)?.Value ?? "";
            var token = headers.Get(VivHeaderContract.InnerRequestToken)?.Value;

            if (!VivRequestToken.TryVerify(
                    token,
                    headers.Get(VivHeaderContract.AppId)?.Value ?? "",
                    headers.Get(VivHeaderContract.SubjectId)?.Value ?? "",
                    headers.Get(VivHeaderContract.UserId)?.Value ?? "",
                    serviceName,
                    secret))
            {
                return null;
            }

            return new VivContextContent
            {
                AppId = appId,
                SubjectId = subjectId,
                UserId = userId
            };
        }

        private static bool TryReadPositiveLong(Metadata headers, string key, out long value)
        {
            value = 0;
            var entry = headers.Get(key);
            return entry != null && long.TryParse(entry.Value, out value) && value > 0;
        }

        private sealed class VivContextScope : IDisposable
        {
            private readonly IVivContext _vivContext;

            public VivContextScope(IVivContext vivContext)
            {
                _vivContext = vivContext;
            }

            public void Dispose()
            {
                _vivContext.Clear();
            }
        }
    }
}
