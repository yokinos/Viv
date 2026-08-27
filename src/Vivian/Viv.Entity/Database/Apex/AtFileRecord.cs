using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 上传文件记录
    /// </summary>
    public class AtFileRecord : EntityBase, ISoftDeleted
    {
        /// <summary>
        /// 所属客户端AppId
        /// </summary>
        public long ClientAppId { get; set; }

        /// <summary>
        /// 文件访问地址
        /// </summary>
        [StringLength(1000)]
        public string? FileUrl { get; set; }

        /// <summary>
        /// 文件MD5，OSS上传返回
        /// </summary>
        [StringLength(64)]
        public string? FileMd5 { get; set; }

        /// <summary>
        /// 文件大小，单位字节
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 文件后缀 jpg/png/pdf等
        /// </summary>
        [StringLength(20)]
        public string? Suffix { get; set; }

        /// <summary>
        /// 来源类型：1轮播 2资讯 3公告 4App配置
        /// </summary>
        public int SourceType { get; set; }

        public EmStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}