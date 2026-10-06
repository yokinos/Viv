using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 工具注册表：所有能被 Agent 调用的工具的权威清单（内置方法 / HTTP 接口 / MCP 工具）。
    /// 与 OtCapabilityBinding 一起构成权限边界 —— 没绑定的工具，模型看不到也调不到。
    /// </summary>
    public class OtTool : EntityBase, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 工具键，也是模型可见的工具名，如 query_order
        /// </summary>
        public string ToolKey { get; set; } = string.Empty;

        /// <summary>
        /// 归属域：apex / herta / deepred / sakumai / ouroboros
        /// </summary>
        public string OwnerDomain { get; set; } = string.Empty;

        /// <summary>
        /// 给模型看的描述 —— 模型选不选这个工具几乎全靠它，必须写清"什么时候用"
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 入参 JSON Schema（前端展示与调用前校验用）
        /// </summary>
        public string? ParamsSchema { get; set; }

        /// <summary>
        /// 传输方式：1=进程内方法 2=HTTP 接口 3=MCP 工具
        /// </summary>
        public int Transport { get; set; }

        /// <summary>
        /// 非进程内时的调用地址
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// 是否需要人工审批：写操作与不可逆操作必须为 true
        /// </summary>
        public bool RequiresApproval { get; set; }

        /// <summary>
        /// 是否只读，用于成本与风险统计
        /// </summary>
        public bool IsReadOnly { get; set; } = true;

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
