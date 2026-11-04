using MagicOnion;

namespace Viv.Apex.Proxy.Contracts
{
    /// <summary>
    /// Apex 客户端应用查询契约（MagicOnion：接口即 IDL，服务端直接实现，没有生成的 Base）。
    /// 其他服务在建立长连接 / 登录时用它核对 AppId 是不是 Apex 里存在且启用的客户端，
    /// 避免把「允许的客户端」抄成各服务自己的配置文件。
    /// </summary>
    public interface IApexAppQueryService : IService<IApexAppQueryService>
    {
        /// <summary>AppId 是否有效（存在且启用）。</summary>
        UnaryResult<ValidateClientAppReply> ValidateClientAppAsync(ValidateClientAppRequest request);

        /// <summary>分页取客户端应用，给定时任务这类“遍历全部应用”的场景用。</summary>
        UnaryResult<GetClientAppPageReply> GetClientAppPageAsync(GetClientAppPageRequest request);
    }
}
