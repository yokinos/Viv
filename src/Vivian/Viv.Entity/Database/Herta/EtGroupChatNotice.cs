using System;
using System.ComponentModel.DataAnnotations;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Herta
{
    /// <summary>
    /// 群公告子表
    /// </summary>
    public class EtGroupChatNotice : EntityBase, ITenant, ISoftDeleted
    {
        /// <summary>
        /// 租户ID
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 群聊Id 关联EtGroupChat.Id
        /// </summary>
        public long GroupChatId { get; set; }

        /// <summary>
        /// 发布人用户Id
        /// </summary>
        public long PublisherUserId { get; set; }

        /// <summary>
        /// 公告标题
        /// </summary>
        [StringLength(200)]
        public string? Title { get; set; }

        /// <summary>
        /// 公告内容
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 是否置顶 1置顶 0不置顶
        /// </summary>
        public int IsTop { get; set; }

        /// <summary>
        /// 公告生效时间
        /// </summary>
        public DateTime StartAt { get; set; }

        /// <summary>
        /// 公告失效时间，null永久有效
        /// </summary>
        public DateTime? EndAt { get; set; }

        /// <summary>
        /// 软删除标记
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}