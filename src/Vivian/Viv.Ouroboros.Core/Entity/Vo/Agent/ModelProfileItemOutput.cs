using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 模型档位列表项（管理接口用），不含密钥明文与密文
    /// </summary>
    public class ModelProfileItemOutput
    {
        /// <summary>
        /// 档位行 Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 档位键
        /// </summary>
        public string ProfileKey { get; set; } = string.Empty;

        /// <summary>
        /// 供应商类型，取 EmProviderType
        /// </summary>
        public int ProviderType { get; set; }

        /// <summary>
        /// 接口地址
        /// </summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>
        /// 模型名
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// 兜底顺序
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 是否已配置密钥（只回有没有，绝不回明文或密文）
        /// </summary>
        public bool HasKey { get; set; }
    }
}
