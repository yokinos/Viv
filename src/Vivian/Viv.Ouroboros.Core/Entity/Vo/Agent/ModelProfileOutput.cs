using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 模型档位解析结果（自检用，不含密钥明文）
    /// </summary>
    public class ModelProfileOutput
    {
        /// <summary>
        /// 档位键
        /// </summary>
        public string ProfileKey { get; set; } = string.Empty;

        /// <summary>
        /// 模型名
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// 接口地址
        /// </summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>
        /// 密钥是否成功解密出来
        /// </summary>
        public bool HasKey { get; set; }

        /// <summary>
        /// 采样温度
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// 最大输出 token
        /// </summary>
        public int? MaxOutputTokens { get; set; }

        /// <summary>
        /// 兜底顺序
        /// </summary>
        public int Priority { get; set; }
    }
}
