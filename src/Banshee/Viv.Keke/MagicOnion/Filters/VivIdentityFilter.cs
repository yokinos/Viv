using Grpc.Core;
using MagicOnion;
using MagicOnion.Server;
using Microsoft.Extensions.DependencyInjection;
using Viv.Contracts;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Keke.Identity;

namespace Viv.Keke.MagicOnion.Filters
{
    /// <summary>
    /// MagicOnion 服务端身份头过滤器 —— gRPC <c>VivGrpcServerInterceptor</c> 的等价物。
    ///
    /// <para>验签与还原逻辑一行都不重写：调用 <see cref="VivIdentityValidator"/>（传输无关），
    /// 这里只做两件事 —— 把结果写进 <see cref="IVivContext"/> / <see cref="LockHolderContext"/>，
    /// 以及把 <see cref="VivIdentityException"/> 映射成 gRPC 的 Unauthenticated。</para>
    ///
    /// <para>必须通过 <c>MagicOnionOptions.GlobalFilters</c> 全局挂载：漏挂不会报错，
    /// 只会让服务端拿到空上下文（租户过滤失效）。</para>
    /// </summary>
    public sealed class VivIdentityFilter : MagicOnionFilterAttribute
    {
        private readonly VivInternalTokenOptions _tokenOptions;

        public VivIdentityFilter(VivInternalTokenOptions tokenOptions)
        {
            _tokenOptions = tokenOptions;
        }

        public override async ValueTask Invoke(ServiceContext context, Func<ServiceContext, ValueTask> next)
        {
            VivIdentityResult result;
            try
            {
                result = VivIdentityValidator.Validate(
                    name => context.CallContext.RequestHeaders.Get(name)?.Value,
                    _tokenOptions);
            }
            catch (VivIdentityException ex)
            {
                // 失败一律 Unauthenticated，不区分缺签名 / 坏签名 / 过期（与 gRPC 侧同口径）。
                throw new RpcException(new Status(StatusCode.Unauthenticated, ex.Message));
            }

            if (result.Context is { } content)
            {
                if (result.HolderId is { } holderId)
                {
                    LockHolderContext.SetHolderId(holderId);
                }

                // 从本次请求的作用域拿 IVivContext：过滤器实例的生命周期不该被用来捕获 Scoped 服务。
                context.ServiceProvider.GetRequiredService<IVivContext>().SetSnapshot(content);
            }

            await next(context).ConfigureAwait(false);
        }
    }
}
