using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Contracts.Options;

namespace Viv.Echo.Grpc
{
    /// <summary>
    /// gRPC 服务端租户上下文恢复拦截器。
    ///
    /// VivContextMiddleware 只对 /api 前缀水合上下文，gRPC 路径必须由此拦截器从 x-viv-* 头恢复。
    /// 未配置 InternalToken 时不信任裸头（不灌上下文，也不拒绝，方便本地没配密钥的服务）。
    /// 配了密钥则先验签：失败一律 <see cref="StatusCode.Unauthenticated"/>，不区分缺签名和坏签名。
    /// 验过之后 AppId 必须为正才灌快照；UserId / SubjectId 可以缺省或为 0（服务间调用往往只有 AppId）。
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
            var content = BuildContext(context.RequestHeaders);
            if (content == null)
            {
                return;
            }

            var vivContext = context.GetHttpContext().RequestServices.GetRequiredService<IVivContext>();
            vivContext.SetSnapshot(content);
        }

        /// <summary>
        /// 无密钥返回 null（调用继续，上下文保持空）。有密钥则用头上的原始字符串验签，
        /// 这样 UserId 写成 "0" 也能对上客户端签出来的载荷。
        /// </summary>
        private VivContextContent? BuildContext(Metadata headers)
        {
            var secret = _tokenOptions.InternalToken;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            var appIdRaw = headers.Get(VivHeaderContract.AppId)?.Value ?? "";
            var subjectRaw = headers.Get(VivHeaderContract.SubjectId)?.Value ?? "";
            var userRaw = headers.Get(VivHeaderContract.UserId)?.Value ?? "";
            var serviceName = headers.Get(VivHeaderContract.ServiceName)?.Value ?? "";
            var holderRaw = headers.Get(VivHeaderContract.HolderId)?.Value ?? "";
            var token = headers.Get(VivHeaderContract.InnerRequestToken)?.Value;

            if (!VivRequestToken.TryVerify(token, appIdRaw, subjectRaw, userRaw, serviceName, holderRaw, secret))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "未通过内部签名校验"));
            }

            if (!long.TryParse(appIdRaw, out var appId) || appId <= 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(holderRaw))
            {
                LockHolderContext.SetHolderId(holderRaw);
            }

            return new VivContextContent
            {
                AppId = appId,
                SubjectId = ReadPositiveLong(subjectRaw),
                UserId = ReadPositiveLong(userRaw),
                HolderId = string.IsNullOrWhiteSpace(holderRaw) ? null : holderRaw
            };
        }

        private static long ReadPositiveLong(string raw)
        {
            return long.TryParse(raw, out var value) && value > 0 ? value : 0;
        }
    }
}
