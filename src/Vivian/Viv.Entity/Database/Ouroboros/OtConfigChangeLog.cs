using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// 配置变更审计：DB 驱动的配置出错概率高于代码 bug，
    /// 改了什么、谁改的、改前改后长什么样，必须留痕，否则线上出问题无法定位也无法回滚。
    /// </summary>
    public class OtConfigChangeLog : EntityBase, ICreatedAt, ICreatedBy
    {
        /// <summary>
        /// 目标表名，如 OtAgent / OtAgentPrompt / OtCapabilityBinding / OtModelProfile
        /// </summary>
        public string TargetTable { get; set; } = string.Empty;

        /// <summary>
        /// 目标业务键（AgentKey / ToolKey / ProfileKey 等）
        /// </summary>
        public string? TargetKey { get; set; }

        /// <summary>
        /// 变更前快照（JSON）
        /// </summary>
        public string? BeforeJson { get; set; }

        /// <summary>
        /// 变更后快照（JSON）
        /// </summary>
        public string? AfterJson { get; set; }

        /// <summary>
        /// 变更说明：改了什么、为什么改
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 创建时间（即变更时间）
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人Id（即操作人）
        /// </summary>
        public long? CreatedBy { get; set; }
    }
}
