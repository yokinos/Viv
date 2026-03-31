using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.Apex
{
    [Table("viv_clientapp")]
    [Serializable]
    public class VivClientApp : EntityBase, ISoftDelete
    {
        /// <summary>
        /// 客户端应用程序名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 客户端平台类型
        /// </summary>
        public EmAppPlatform Platform { get; set; }

        /// <summary>
        /// 密钥
        /// </summary>
        public string? AppSecret { get; set; }

        /// <summary>
        /// 应用来源
        /// </summary>
        public EmAppSouce Source { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateAt { get; set; }

        /// <summary>
        /// 创建人
        /// </summary>
        public long CreateBy { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateAt { get; set; }

        /// <summary>
        /// 更新人
        /// </summary>
        public long UpdateBy { get; set; }


        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
