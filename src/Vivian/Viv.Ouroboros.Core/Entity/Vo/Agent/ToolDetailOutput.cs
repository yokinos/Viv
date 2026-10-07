using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.Entity.Vo.Agent
{
    /// <summary>
    /// 工具详情（管理接口用）
    /// </summary>
    public class ToolDetailOutput
    {
        /// <summary>
        /// 工具行 Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 工具键
        /// </summary>
        public string ToolKey { get; set; } = string.Empty;

        /// <summary>
        /// 归属域
        /// </summary>
        public string OwnerDomain { get; set; } = string.Empty;

        /// <summary>
        /// 给模型看的描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 入参 JSON Schema
        /// </summary>
        public string? ParamsSchema { get; set; }

        /// <summary>
        /// 传输方式，取 EmToolTransport
        /// </summary>
        public int Transport { get; set; }

        /// <summary>
        /// 调用地址
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// 是否需要人工审批
        /// </summary>
        public bool RequiresApproval { get; set; }

        /// <summary>
        /// 是否只读
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

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
