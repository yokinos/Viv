using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// Agent 详情（管理接口用）
    /// </summary>
    public class AgentDetailOutput
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
        /// 什么时候该用这个 Agent
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 生效的提示词版本号
        /// </summary>
        public int ActivePromptVersion { get; set; }

        /// <summary>
        /// 使用的模型档位键
        /// </summary>
        public string ModelProfile { get; set; } = string.Empty;

        /// <summary>
        /// 工具回环上限
        /// </summary>
        public int MaxToolIterations { get; set; }

        /// <summary>
        /// 跨进程执行地址
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 定义版本号，配置变更时递增
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 是否已软删
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
