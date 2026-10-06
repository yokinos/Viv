using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// Agent 装配结果（自检用）
    /// </summary>
    public class AgentInfoOutput
    {
        /// <summary>
        /// Agent 标识
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Agent 名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Agent 描述
        /// </summary>
        public string? Description { get; set; }
    }
}
