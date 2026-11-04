using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Echo.Identity;

namespace Viv.Echo.Grpc
{
    /// <summary>
    /// gRPC 服务端租户上下文恢复拦截器。
    ///
    /// VivContextMiddleware 只对 /api 前缀水合上下文，gRPC 路径必须由此拦截器从 x-viv-* 头恢复。
    /// 验签与还原逻辑在 <see cref="VivIdentityValidator"/>（传输无关，MagicOnion 的 Filter 用同一份），
    /// 这里只负责把结果写进 DI 里的 <see cref="IVivContext"/> 与 <see cref="LockHolderContext"/>，
    /// 并把 <see cref="VivIdentityException"/> 映射成 gRPC 的 Unauthenticated。
    ///
    /// 未配置 InternalToken 时不信任裸头（不灌上下文，也不拒绝，方便本地没配密钥的服务）。
    ///
    /// 这里不清理上下文。LocalEventFlushMiddleware 挂在拦截器外面，提前 Clear 会让 gRPC 上的
    /// 本地事件处理器看不到租户。请求结束由外层 VivContextMiddleware / RequestTrackMiddleware 清。
    /// </summary>
    public class VivGrpcServerInterceptor : Interceptor
    {
        private readonly VivInternalTokenOptions _tokenOptions;

        public VivGrpcServerInterceptor(VivInternalTokenOptions tokenOptions)
        {
            _tokenOptions = tokenOptions;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            ApplyVivContext(context);
            return await continuation(request, context).ConfigureAwait(false);
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            ApplyVivContext(context);
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }

        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            ServerCallContext context,
            ClientStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            ApplyVivContext(context);
            return await continuation(requestStream, context).ConfigureAwait(false);
        }

        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            DuplexStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            ApplyVivContext(context);
            await continuation(requestStream, responseStream, context).ConfigureAwait(false);
        }

        private void ApplyVivContext(ServerCallContext context)
        {
            VivIdentityResult result;
            try
            {
                result = VivIdentityValidator.Validate(
                    name => context.RequestHeaders.Get(name)?.Value,
                    _tokenOptions);
            }
            catch (VivIdentityException ex)
            {
                // 失败一律 Unauthenticated，不区分缺签名 / 坏签名 / 过期。
                throw new RpcException(new Status(StatusCode.Unauthenticated, ex.Message));
            }

            if (result.Context is not { } content)
            {
                return;
            }

            if (result.HolderId is { } holderId)
            {
                LockHolderContext.SetHolderId(holderId);
            }

            context.GetHttpContext()
                .RequestServices
                .GetRequiredService<IVivContext>()
                .SetSnapshot(content);
        }
    }
}
