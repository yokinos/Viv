using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// Agent 列表项（管理接口用）
    /// </summary>
    public class AgentItemOutput
    {
        /// <summary>
        /// Agent 行 Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 业务键
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// Agent 类型，取 EmAgentType
        /// </summary>
        public int AgentType { get; set; }

        /// <summary>
        /// 归属域
        /// </summary>
        public string OwnerDomain { get; set; } = string.Empty;

        /// <summary>
        /// 展示名称
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// 生效的提示词版本号
        /// </summary>
        public int ActivePromptVersion { get; set; }

        /// <summary>
        /// 使用的模型档位键
        /// </summary>
        public string ModelProfile { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 是否已软删
        /// </summary>
        public bool IsDeleted { get; set; }
    }
}
