using System;
using System.Collections.Generic;
using System.Text;
using Viv.Delusion.Magic;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// 信封
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class NanaEnvelope<T> where T : NanaEvent
    {
        public NanaEnvelope() { }

        /// <summary>
        /// 消息Id 也就是邮戳的了
        /// </summary>
        public long MessageId { get; set; } = IdMagic.NextId();

        /// <summary>
        /// 由哪个客户端的请求产生的消息,在这套设计下即使是定时任务站点也被视为客户端
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// SaaS类型的Id
        /// </summary>
        public long TenantId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public T? Content { get; set; }

        /// <summary>
        /// 消息创建时间
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
