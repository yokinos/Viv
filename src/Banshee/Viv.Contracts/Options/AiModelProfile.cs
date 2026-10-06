using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Options
{
    /// <summary>
    /// 一个模型档位的连接与参数 —— 机制层，不含任何业务语义。
    /// 档位数据从哪来（数据库 / appsettings / 代码）由使用方决定；框架只负责"给了档位就能造客户端"。
    /// </summary>
    public class AiModelProfile
    {
        /// <summary>
        /// 档位键，如 main（主 Agent）/ sub（子 Agent）/ router（分诊与工具选择）/
        /// summarize（上下文压缩）/ vision（带图）
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
        /// 已解密的明文密钥，只在内存中传递，不落日志、不落库
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// 模型名，如 deepseek-chat / deepseek-reasoner
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// 采样温度，为空则用供应商默认值
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// 单次回复最大输出 token，为空则用供应商默认值
        /// </summary>
        public int? MaxOutputTokens { get; set; }

        /// <summary>
        /// 单次调用超时秒数
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 同档位多行时的兜底顺序，小的优先
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }
    }
}
