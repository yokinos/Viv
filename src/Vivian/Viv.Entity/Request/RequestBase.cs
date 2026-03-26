using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Viv.Entity;

namespace Viv.Entity.Request
{
    /// <summary>
    /// 请求基类
    /// </summary>
    [Serializable]
    public class RequestBase : IApiRequest
    {
        /// <summary>
        /// [必传]客户端AppId
        /// </summary>
        [Required]
        public long AppId { get; set; }

        /// <summary>
        /// [必传]服务器内部版本号
        /// </summary>
        [Required]
        public int Version { get; set; }

        /// <summary>
        /// [必传]时间戳（防重放攻击）
        /// </summary>
        [Required]
        public long Timestamp { get; set; }

        /// <summary>
        ///[必传]签名字段
        /// </summary>
        [Required]
        public string Sign { get; set; } = string.Empty;

        /// <summary>
        /// [可选]其他扩展参数
        /// </summary>
        public Dictionary<string, object> Expand { get; set; } = [];

        public string Validate()
        {
            if (AppId <= 0 || Version <= 0)
            {
                return "AppId和Version不能为空";
            }

            return string.Empty;
        }
    }
}
