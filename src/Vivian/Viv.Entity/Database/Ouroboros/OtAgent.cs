using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Ouroboros
{
    /// <summary>
    /// Agent 定义：主 Agent 与子 Agent 统一一张表。
    /// 主 Agent 由 Ouroboros 自己维护；子 Agent 的 OwnerDomain 指向它所属的域服务，
    /// 有几个主 Agent、每个主 Agent 挂哪些子 Agent，全部由数据库决定，不发版。
    /// </summary>
    public class OtAgent : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 业务键，如 ouroboros_main_support / apex_user
        /// </summary>
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// Agent 类型：1=主 Agent 2=子 Agent
        /// </summary>
        public int AgentType { get; set; }

        /// <summary>
        /// 归属域：ouroboros / apex / herta / deepred / sakumai
        /// </summary>
        public string OwnerDomain { get; set; } = string.Empty;

        /// <summary>
        /// 展示名称
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// 什么时候该用这个 Agent —— 主 Agent 选子 Agent 就靠这句话，必填且要写具体
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 当前生效的提示词版本号（指向 OtAgentPrompt.Version）
        /// </summary>
        public int ActivePromptVersion { get; set; }

        /// <summary>
        /// 使用的模型档位（对应 OtModelProfile.ProfileKey）
        /// </summary>
        public string ModelProfile { get; set; } = "sub";

        /// <summary>
        /// 工具回环上限，防止模型反复调工具停不下来
        /// </summary>
        public int MaxToolIterations { get; set; } = 8;

        /// <summary>
        /// 跨进程执行地址（子 Agent 用），如 http://viv.apex.api/v1/agent/sub/apex_user
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 定义版本号，配置变更时递增，用于让运行时缓存失效
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 是否删除（软删标记）
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }

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
