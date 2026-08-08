using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Models
{
    /// <summary>
    /// Viv 自定义 JWT Claim 类型。签发（JwtTokenService）、网关透传、下游解析共用，
    /// 避免魔法字符串漂移。与 RequestTokenAnalysisMagic 的 x-viv-* Header 契约一一对应。
    /// </summary>
    public static class VivClaimTypes
    {
        /// <summary>
        /// 客户端 AppId —— 网关透传给下游的 x-viv-appId 头
        /// </summary>
        public const string AppId = "AppId";

        /// <summary>
        /// 多租户 TenantId —— 下游的 SubjectId 即此值（x-viv-subjectId 头）
        /// </summary>
        public const string TenantId = "TenantId";
    }
}
