using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Echo.Grpc
{
    /// <summary>
    /// gRPC 服务端租户上下文恢复拦截器。
    ///
    /// 为什么需要：<c>VivContextMiddleware</c> 只对 <c>/api</c> 前缀请求水合上下文
    /// （DefaultVivContextProvider.ShouldSkip），gRPC 端点路径（如
    /// <c>/Viv.ServiceProxy.Protos.TenantGrpcService/GetTenant</c>）会被跳过，
    /// 必须由本拦截器在调用进入服务实现前，从客户端注入的 <c>x-viv-*</c> 请求头恢复租户上下文
    /// （SetSnapshot 写 IVivContextAccessor 的 AsyncLocal，EF 租户过滤可读到），调用结束 Clear。
    ///
    /// 注意：v1 信任 x-viv-* 头（对齐 VivContextMiddleware 语义，不验 x-request-token HMAC），
    /// gRPC 端口不应公网暴露；HMAC 验签列为后续项。
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

        /// <summary>从请求头恢复租户上下文，返回作用域释放器（请求结束 Clear）。</summary>
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
        /// 对齐 RequestTokenResolver 语义：AppId + UserId 必须为正才认头；SubjectId 可选。
        /// Metadata.Get 不区分大小写，直接读契约常量。
        /// </summary>
        private static VivContextContent? TryBuildContext(Metadata headers)
        {
            if (!TryReadPositiveLong(headers, VivHeaderContract.AppId, out var appId))
            {
                return null;
            }

            if (!TryReadPositiveLong(headers, VivHeaderContract.UserId, out var userId))
            {
                return null;
            }

            TryReadPositiveLong(headers, VivHeaderContract.SubjectId, out var subjectId);
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

            public void Dispose() => _vivContext.Clear();
        }
    }
}
