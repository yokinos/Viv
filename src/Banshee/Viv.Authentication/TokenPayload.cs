using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Authentication
{
    /// <summary>
    /// 令牌载荷模型（统一JWT/PASETO的载荷数据）
    /// </summary>
    public class TokenPayload
    {
        /// <summary>
        /// 隶属Viv平台的哪个App
        /// </summary>
        public string VivAppId { get; set; } = string.Empty;

        /// <summary>
        /// 多租户Id
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>
        /// 用户ID
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// 用户名
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// 角色列表
        /// </summary>
        public List<string> Roles { get; set; } = [];

        /// <summary>
        /// 自定义扩展字段
        /// </summary>
        public Dictionary<string, string> Extensions { get; set; } = [];
    }
}
