using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// Agent 提示词版本：改提示词必须新增版本，靠 OtAgent.ActivePromptVersion 切换，
    /// 出问题能一秒回滚，也能查到"当时用的是哪一版"。
    /// </summary>
    public class OtAgentPrompt : EntityBase, ICreatedAt, ICreatedBy
    {
        /// <summary>
        /// Agent 业务键（OtAgent.AgentKey）
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 版本号，同一 AgentKey 下递增
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// 系统提示词正文
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 这台 Agent 自己的补充说明（能力边界、语气、拒答规则等）
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// 变更备注：这一版改了什么、为什么改
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
    }
}
