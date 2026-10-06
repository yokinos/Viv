using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// token 用量日聚合：按 日期 + 主体 + Agent + 档位 + 模型 五个维度，
    /// 出账时才能回答"钱花在谁身上、花在哪个 Agent 上、哪个模型最贵"。
    /// 由 OtMessage 定时 rollup（Viv.Clockwork）或写入时增量累加。
    /// </summary>
    public class OtTokenUsageDaily : EntityBase, ICreatedAt, IUpdatedAt
    {
        /// <summary>
        /// 统计日期（按天截断）
        /// </summary>
        public DateTime StatDate { get; set; }

        /// <summary>
        /// 隔离主体（subjectId）
        /// </summary>
        public long? SubjectId { get; set; }

        /// <summary>
        /// Agent 业务键（主 Agent 或子 Agent）
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// 模型档位（OtModelProfile.ProfileKey）
        /// </summary>
        public string? ModelProfile { get; set; }

        /// <summary>
        /// 实际模型名
        /// </summary>
        public string? ModelId { get; set; }

        /// <summary>
        /// 调用次数
        /// </summary>
        public long CallCount { get; set; }

        /// <summary>
        /// 输入 token 合计
        /// </summary>
        public long InputTokens { get; set; }

        /// <summary>
        /// 输出 token 合计
        /// </summary>
        public long OutputTokens { get; set; }

        /// <summary>
        /// 命中缓存的输入 token 合计
        /// </summary>
        public long CachedInputTokens { get; set; }

        /// <summary>
        /// 推理 token 合计
        /// </summary>
        public long ReasoningTokens { get; set; }

        /// <summary>
        /// 估算费用（按档位的单价计算）
        /// </summary>
        public decimal EstimatedCost { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 更新时间（最近一次 rollup 时间）
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}
