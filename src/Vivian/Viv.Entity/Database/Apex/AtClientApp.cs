using System;
using System.ComponentModel.DataAnnotations;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 客户端应用表
    /// 设计：每个客户端应用对应一个独立 AppId
    /// 同一应用不同平台 = 不同 AppId
    /// 同一应用不同版本 = 共用同一个 AppId（版本管理见 <see cref="AtClientAppVersion"/>）
    /// </summary>
    public class AtClientApp : EntityBase, ISoftDeleted, ICreatedAt, ICreatedBy, IUpdatedAt, IUpdatedBy
    {
        /// <summary>
        /// 对外唯一鉴权标识 AppKey
        /// 只用于「客户端自报身份」这一个入口；内部关联（菜单、版本、组织授权等）一律用 <see cref="EntityBase.Id"/>，
        /// </summary>
        [StringLength(64)]
        public string? AppKey { get; set; }

        /// <summary>
        /// 客户端应用程序名称
        /// </summary>
        [StringLength(100)]
        public string? Name { get; set; }

        /// <summary>
        /// 客户端平台类型
        /// </summary>
        public EmAppPlatform Platform { get; set; }

        /// <summary>
        /// 菜单适配的前端类型
        /// </summary>
        public EmClientStack ClientStack { get; set; }

        /// <summary>
        /// 密钥
        /// </summary>
        [StringLength(512)]
        public string? AppSecret { get; set; }

        /// <summary>
        /// 应用来源
        /// </summary>
        public EmAppSouce Source { get; set; }

        /// <summary>
        /// Android 应用包名
        /// </summary>
        [StringLength(100)]
        public string? PackageName { get; set; }

        /// <summary>
        /// iOS 应用BundleId
        /// </summary>
        [StringLength(100)]
        public string? BundleId { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        [StringLength(500)]
        public string? Remark { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EmStatus Status { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public long? CreatedBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

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