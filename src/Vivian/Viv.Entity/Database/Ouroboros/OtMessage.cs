using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 消息（高频追加，不做软删）。
    /// token 必须分项记录：缓存命中的输入与推理 token 计价与普通 token 不同。
    /// 注意：工具结果不在 Text 里，它在 MAF/MEAI 的 FunctionResultContent 中，落库时要单独取。
    /// </summary>
    public class OtMessage : EntityBase, ICreatedAt
    {
        /// <summary>
        /// 所属会话（OtConversation.Id）
        /// </summary>
        public long ConversationId { get; set; }

        /// <summary>
        /// 会话内自增序号，保证顺序可复原
        /// </summary>
        public int Seq { get; set; }

        /// <summary>
        /// 角色，取 <see cref="EmMessageRole"/>
        /// </summary>
        public EmMessageRole Role { get; set; } = EmMessageRole.User;

        /// <summary>
        /// 正文（按业务决定是否脱敏，当前直接存明文）
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 内容类型，取 <see cref="EmMessageContentType"/>
        /// </summary>
        public EmMessageContentType? ContentType { get; set; }

        /// <summary>
        /// 产生这条消息的 Agent（主 Agent 或子 Agent 的业务键）
        /// </summary>
        public string? AgentKey { get; set; }

        /// <summary>
        /// 使用的模型档位（OtModelProfile.ProfileKey）
        /// </summary>
        public string? ModelProfile { get; set; }

        /// <summary>
        /// 实际返回该消息的模型名
        /// </summary>
        public string? ModelId { get; set; }

        /// <summary>
        /// 本次调用消耗的输入 token
        /// </summary>
        public int? InputTokens { get; set; }

        /// <summary>
        /// 本次调用消耗的输出 token
        /// </summary>
        public int? OutputTokens { get; set; }

        /// <summary>
        /// 命中缓存的输入 token（计价更低，单独统计）
        /// </summary>
        public int? CachedInputTokens { get; set; }

        /// <summary>
        /// 推理 token（带推理能力的模型才有，单独计价）
        /// </summary>
        public int? ReasoningTokens { get; set; }

        /// <summary>
        /// 本次调用耗时（毫秒）
        /// </summary>
        public int? LatencyMs { get; set; }

        /// <summary>
        /// 结束原因（stop / length / tool_calls 等）
        /// </summary>
        public string? FinishReason { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }
    }
}
