using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 能力绑定详情（管理接口用）
    /// </summary>
    public class CapabilityBindingDetailOutput
    {
        /// <summary>
        /// 绑定行 Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 宿主 Agent 业务键
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 能力类型，取 EmCapabilityType
        /// </summary>
        public int CapabilityType { get; set; }

        /// <summary>
        /// 能力键
        /// </summary>
        public string CapabilityKey { get; set; } = string.Empty;

        /// <summary>
        /// 暴露给模型的名字
        /// </summary>
        public string? ExposedName { get; set; }

        /// <summary>
        /// 暴露给模型的描述
        /// </summary>
        public string? ExposedDescription { get; set; }

        /// <summary>
        /// 排序
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否覆盖能力自身的审批设置；null = 沿用能力自身
        /// </summary>
        public bool? RequiresApproval { get; set; }

        /// <summary>
        /// 主体白名单 JSON 数组，空白 = 全部主体可用
        /// </summary>
        public string? AllowedSubjectIds { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

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
