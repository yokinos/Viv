using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 客户端应用版本
    /// </summary>
    public class AtClientAppVersion : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 客户端AppId
        /// </summary>
        public long? AppId { get; set; }

        /// <summary>
        /// 版本号（不为空 且自增）
        /// </summary>
        public int VersionCode { get; set; }

        /// <summary>
        /// 更新说明
        /// </summary>
        public string? UpdateRemark { get; set; }

        /// <summary>
        /// 版本
        /// </summary>
        public string? Version { get; set; }

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
