using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Interface;

namespace Viv.Herta.Core.Entity.Message
{
    /// <summary>
    /// Viv下的Chat 消息体
    /// </summary>
    public class HertaChatMessage
    {
        /// <summary>
        /// 客户端AppId,标识由哪个客户端发的消息
        /// 如果时是系统模拟发出的消息 则为0
        /// </summary>
        public long AppId { get; set; }

        /// <summary>
        /// 消息Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 消息发送者的Id
        /// </summary>
        public long FromUserId { get; set; }

        /// <summary>
        /// 消息接收者的Id
        /// </summary>
        public long ToUserId { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public IChatMessage? Body { get; set; }

        /// <summary>
        /// 消息发送的时间
        /// </summary>
        public DateTimeOffset SentAt { get; set; }

        /// <summary>
        /// 消息签名
        /// </summary>
        public string? Sign { get; set; }
    }
}
