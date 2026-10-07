using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 提示词详情（管理接口用）
    /// </summary>
    public class AgentPromptDetailOutput
    {
        /// <summary>
        /// 提示词行 Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Agent 业务键
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 版本号
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// 是否是该 Agent 当前生效的版本
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 系统提示词正文
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 补充说明
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// 变更备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }
}
