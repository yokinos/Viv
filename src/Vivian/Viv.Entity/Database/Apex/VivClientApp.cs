using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Viv.Entity.Enums;
using Viv.Momo.Base;

namespace Viv.Entity.Database.Apex
{
    [Table("viv_clientapp")]
    [Serializable]
    public class VivClientApp : EntityBase
    {
        /// <summary>
        /// 客户端应用程序名称
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 客户端平台类型
        /// </summary>
        public VxAppPlatform Platform { get; set; }

        /// <summary>
        /// 密钥
        /// </summary>
        public string? AppSecret { get; set; }

        /// <summary>
        /// 应用来源
        /// </summary>
        public VxAppSource Source { get; set; }
    }
}
