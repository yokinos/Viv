using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Viv.Momo.Base;

namespace Viv.Entity.Database.Apex
{
    /// <summary>
    /// 客户端应用版本
    /// </summary>
    [Table("viv_clientapp_version")]
    public class VivClientAppVersion : EntityBase
    {
        /// <summary>
        /// 客户端AppId
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 更新说明
        /// </summary>
        public string? UpdateRemark { get; set; }

        /// <summary>
        /// 版本
        /// </summary>
        public string? Version { get; set; }
    }
}
