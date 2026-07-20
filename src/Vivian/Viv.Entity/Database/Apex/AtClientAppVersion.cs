using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 客户端应用版本表
    /// 一对多关联 AtClientApp，一个应用多条版本记录
    /// </summary>
    public class AtClientAppVersion : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 关联客户端应用主键Id（AtClientApp.Id）
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 数字版本号，用于版本大小对比，自增
        /// </summary>
        public int VersionCode { get; set; }

        /// <summary>
        /// 展示版本号 如 1.0.2
        /// </summary>
        [StringLength(32)]
        public string? Version { get; set; }

        /// <summary>
        /// 更新说明
        /// </summary>
        [StringLength(2000)]
        public string? UpdateRemark { get; set; }

        /// <summary>
        /// 安装包OSS下载地址
        /// </summary>
        [StringLength(800)]
        public string? DownloadUrl { get; set; }

        /// <summary>
        /// 更新类型 0可选更新 1强制更新
        /// </summary>
        public EmClientUpdateType UpdateType { get; set; }

        /// <summary>
        /// 版本状态 0草稿 1灰度发布 2全量上线 3下线停用
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public long? UpdatedBy { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// 删除时间
        /// </summary>
        public DateTime? DeletedAt { get; set; }
    }
}