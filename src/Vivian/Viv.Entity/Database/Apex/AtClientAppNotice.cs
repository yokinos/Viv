using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 客户端公告广播表
    /// 每条公告绑定单个ClientApp，实现不同App推送不同公告
    /// </summary>
    public class AtClientAppNotice : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 绑定客户端AppId（关联AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 可见范围类型：全局/组织/租户
        /// </summary>
        public EmNoticeBindType BindType { get; set; }

        /// <summary>
        /// 绑定对象ID集合，逗号分隔
        /// Global固定"0"；Org存储多个OrgId；Tenant存储多个TenantId
        /// </summary>
        [StringLength(2000)]
        public string BindIds { get; set; } = "0";

        /// <summary>
        /// 公告标题
        /// </summary>
        [StringLength(200)]
        public string? Title { get; set; }

        /// <summary>
        /// 公告正文富文本
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 封面图
        /// </summary>
        [StringLength(1000)]
        public string? CoverUrl { get; set; }

        /// <summary>
        /// 是否弹窗推送
        /// </summary>
        public bool IsPopup { get; set; }

        /// <summary>
        /// 是否置顶
        /// </summary>
        public bool IsTop { get; set; }

        /// <summary>
        /// 生效时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 失效时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public long? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}