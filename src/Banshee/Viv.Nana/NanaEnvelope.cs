using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Models;
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
        /// Viv的上下文信息
        /// </summary>
        public VivContextContent? Context { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public T? Content { get; set; }

        /// <summary>
        /// 是否是延迟消息,并且记录延迟时间
        /// </summary>
        public double? DelaySecond { get; set; }

        /// <summary>
        /// 重新投递的次数
        /// </summary>
        public int ReDeliverCount { get; set; }

        /// <summary>
        /// 消息创建时间
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
