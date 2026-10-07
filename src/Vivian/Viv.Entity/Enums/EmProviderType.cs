using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Enums
{
    /// <summary>
    /// 模型供应商类型（OtModelProfile.ProviderType）
    /// </summary>
    public enum EmProviderType
    {
        /// <summary>
        /// OpenAI 兼容（DeepSeek 等）
        /// </summary>
        OpenAiCompatible = 1,

        /// <summary>
        /// Azure OpenAI
        /// </summary>
        AzureOpenAi = 2,

        /// <summary>
        /// 其它
        /// </summary>
        Other = 3
    }
}
