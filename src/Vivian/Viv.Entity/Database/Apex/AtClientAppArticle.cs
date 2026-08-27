using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 资讯文章详情表
    /// </summary>
    public class AtClientAppArticle : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 关联客户端AppId集合，多个Id使用逗号分隔
        /// </summary>
        [StringLength(2000)]
        public string? ClientAppIds { get; set; }

        /// <summary>
        /// 文章标题
        /// </summary>
        [StringLength(100)]
        public string? Title { get; set; }

        /// <summary>
        /// 文章富文本内容
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 备注说明
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        public EmStatus Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public long? CreatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long? UpdatedBy { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAt { get; set; }
    }
}