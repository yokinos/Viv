using Grpc.Core;
using MagicOnion.Client;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Keke.Identity;

namespace Viv.Keke.MagicOnion.Filters
{
    /// <summary>
    /// MagicOnion 客户端身份头过滤器 —— gRPC <c>VivGrpcInterceptor</c> 的等价物。
    ///
    /// <para>值来自 <see cref="VivIdentityHeaders"/>（与 gRPC 拦截器共用同一份签名实现），
    /// 这里只负责写进 gRPC metadata。</para>
    ///
    /// <para>两个与 gRPC 拦截器不同的地方：</para>
    /// <list type="number">
    /// <item><description>写“覆盖”而不是“缺了才加”：MagicOnion 的 <c>CallOptions</c> 挂在客户端实例上、
    /// 会被复用，签名又带时间戳，追加式写入会让请求带上一串过期 token。</description></item>
    /// <item><description>依赖 <see cref="IVivContextAccessor"/>（单例 + AsyncLocal）而不是 Scoped 的
    /// <see cref="IVivContext"/>：过滤器实例绑在单例客户端上，捕获 Scoped 服务就是 captive dependency。</description></item>
    /// </list>
    /// </summary>
    public sealed class VivIdentityClientFilter : IClientFilter
    {
        private readonly IVivContextAccessor _accessor;

        private readonly VivInternalTokenOptions _tokenOptions;

        public VivIdentityClientFilter(IVivContextAccessor accessor, VivInternalTokenOptions tokenOptions)
        {
            _accessor = accessor;
            _tokenOptions = tokenOptions;
        }

        public async ValueTask<ResponseContext> SendAsync(RequestContext context, Func<RequestContext, ValueTask<ResponseContext>> next)
        {
            var values = VivIdentityHeaders.Build(_accessor.Current, _tokenOptions);

            var headers = context.CallOptions.Headers;
            if (headers is null)
            {
                // RequestContext.CallOptions 是只读属性，拿不到就写不回去；
                // 静默跳过会变成“服务端按未认证拒绝”，这里直接抛，避免把配置问题伪装成鉴权失败。
                throw new InvalidOperationException("MagicOnion RequestContext.CallOptions.Headers 未初始化，无法注入 x-viv-* 身份头。");
            }

            foreach (var pair in values.ToHeaderPairs())
            {
                // 覆盖：复用同一个客户端实例时不能让旧值/旧签名累积。
                for (var i = headers.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(headers[i].Key, pair.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        headers.RemoveAt(i);
                    }
                }

                headers.Add(pair.Key, pair.Value);
            }

            return await next(context).ConfigureAwait(false);
        }
    }
}
