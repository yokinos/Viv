using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Enums;
using Viv.Nana.Core;

namespace Viv.EventContracts.Apex.Logging
{
    public class UserOperationLogEvent : NanaEvent
    {
        public UserOperationLogEvent() { }

        /// <summary>
        /// 功能模块
        /// </summary>
        public EmOperationModule Module { get; set; }

        /// <summary>
        /// 操作类型
        /// </summary>
        public EmOperationType Operation { get; set; }

        /// <summary>
        /// 业务操作描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 请求参数
        /// </summary>
        public string? RequestJson { get; set; }

        /// <summary>
        /// 返回Json
        /// </summary>
        public string? ResponseJson { get; set; }
    }
}
