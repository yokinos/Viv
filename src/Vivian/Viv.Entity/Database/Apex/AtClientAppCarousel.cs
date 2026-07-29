using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Entity.Interface;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// App首页轮播图表
    /// 平台类App首页广告Banner，绑定指定App
    /// </summary>
    public class AtClientAppCarousel : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 所属客户端AppId
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 轮播标题
        /// </summary>
        [StringLength(200)]
        public string? Title { get; set; }

        /// <summary>
        /// 轮播图片地址
        /// </summary>
        [StringLength(1000)]
        public string? ImageUrl { get; set; }

        /// <summary>
        /// 轮播位置
        /// </summary>
        public byte? Position { get; set; }

        /// <summary>
        /// 跳转类型
        /// </summary>
        public EmCarouselJumpType JumpType { get; set; }

        /// <summary>
        /// 跳转目标Id
        /// </summary>
        [StringLength(1000)]
        public string? JumpId { get; set; }

        /// <summary>
        /// 生效时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 失效时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 排序，数字越大越靠前
        /// </summary>
        public int Sort { get; set; }

        public EmStatus Status { get; set; }

        [StringLength(500)]
        public string? Remark { get; set; }

        public DateTime CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public long? UpdatedBy { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}