using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;
using Viv.Vva.Magic;

namespace Viv.Nana.Models
{
    public class VivMessage<T> where T : NanaMessage
    {
        /// <summary>
        /// 消息Id
        /// </summary>
        public long MessageId { get; set; } = IdMagic.NextId();

        /// <summary>
        /// Viv的AppId
        /// </summary>
        public long VivAppId { get; set; }

        /// <summary>
        /// SaaS类型的Id
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public T? Content { get; set; }
    }
}
