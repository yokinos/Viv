using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Models
{
    /// <summary>
    /// 令牌载荷模型（统一JWT/PASETO的载荷数据）
    /// </summary>
    public class TokenPayload
    {
        /// <summary>
        /// Viv下的客户端AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 多租户Id(不一定会有)
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 用户Id 
        /// </summary>
        public long UserId { get; set; }

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
