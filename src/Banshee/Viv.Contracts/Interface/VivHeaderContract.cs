namespace Viv.Contracts.Interface
{
    /// <summary>
    /// gRPC / HTTP 跨层共用上下文头契约（单一来源）。
    /// HTTP 头大小写不敏感；gRPC metadata 键写盘需 <see cref="string.ToLowerInvariant"/>，
    /// 读端 Metadata.Get 不区分大小写，可直接用常量。
    /// 网关认证后回填、下游服务端拦截器据此恢复租户上下文。
    /// </summary>
    public static class VivHeaderContract
    {
        /// <summary>客户端 AppId</summary>
        public const string AppId = "x-viv-appId";

        /// <summary>主体 Id（TenantId / CompanyId / OrgId）</summary>
        public const string SubjectId = "x-viv-subjectId";

        /// <summary>当前登录用户 Id</summary>
        public const string UserId = "x-viv-userId";

        /// <summary>服务名，如 viv.apex.api</summary>
        public const string ServiceName = "x-viv-serviceName";

        /// <summary>内部请求签名 Token（HMAC）</summary>
        public const string InnerRequestToken = "x-request-token";
    }
}
