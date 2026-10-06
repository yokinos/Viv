using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 模型档位：一个 ProfileKey（main/sub/router/summarize/vision）可配多行，
    /// 按 Priority 顺序做兜底（前一行的供应商挂了就走下一行）。
    /// 全部字段落库而不是写 appsettings —— 供应商与模型会变，改配置不该发版。
    /// </summary>
    public class OtModelProfile : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 档位键：main（主 Agent）/ sub（子 Agent）/ router（分诊与工具选择）/
        /// summarize（上下文压缩摘要）/ vision（带图）
        /// </summary>
        public string ProfileKey { get; set; } = string.Empty;

        /// <summary>
        /// 供应商类型：1=OpenAI 兼容（DeepSeek 等）2=Azure OpenAI 3=其它
        /// </summary>
        public int ProviderType { get; set; }

        /// <summary>
        /// 接口地址，如 https://api.deepseek.com/v1
        /// </summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>
        /// 密钥密文（Viv.Delusion.EncryptMagic 加密），禁止存明文
        /// </summary>
        public string? ApiKeyCipher { get; set; }

        /// <summary>
        /// 模型名，如 deepseek-chat / deepseek-reasoner
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// 采样温度，为空则用供应商默认值
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// 单次回复的最大输出 token，为空则用供应商默认值
        /// </summary>
        public int? MaxOutputTokens { get; set; }

        /// <summary>
        /// 单次调用超时秒数
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 同一档位多行时的兜底顺序，小的优先
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人Id
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 更新人Id
        /// </summary>
        public long? UpdatedBy { get; set; }
    }
}
