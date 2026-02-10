using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Models;

namespace Viv.Nana.Core
{
    public class PublishEvent<T> where T : VivMessage
    {
        public PublishResultType ResultType { get; set; }

        public string Message { get; set; }

        public NanaMessage<T>? Content { get; set; }

        public PublishEvent(PublishResultType resultType, string message = "", NanaMessage<T>? content = null)
        {
            ResultType = resultType;
            Message = message;
            Content = content;
        }
    }

    public enum PublishResultType
    {
        /// <summary>
        /// 成功
        /// </summary>
        Ack,

        /// <summary>
        /// 未确认
        /// </summary>
        Nacks,

        /// <summary>
        /// 退回
        /// </summary>
        Return,
    }
}
