using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// Agent 会话状态快照：MAF 的 AgentSession 序列化结果。
    ///
    /// 为什么不能用 OtMessage 代替：审批与工具调用是**结构化内容**
    /// （ToolApprovalRequestContent / FunctionCallContent），存成纯文本恢复不回去。
    /// OtMessage 的定位是审计与展示，恢复的真相源是这张表。
    /// </summary>
    public class OtAgentSessionState : EntityBase, ICreatedAt, IUpdatedAt
    {
        /// <summary>
        /// 所属会话（OtConversation.Id）
        /// </summary>
        public long ConversationId { get; set; }

        /// <summary>
        /// Agent 业务键 —— 一个会话里主 Agent 与各子 Agent 各有自己的会话状态
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// MAF AgentSession 的序列化 JSON
        /// </summary>
        public string StateJson { get; set; } = string.Empty;

        /// <summary>
        /// 快照结构版本，将来 MAF 改了序列化结构可以据此判断兼容性
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间（最近一次快照时间）
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
