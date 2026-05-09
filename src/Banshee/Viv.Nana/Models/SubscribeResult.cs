using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Nana.Models
{
    public class SubscribeResult
    {
        /// <summary>
        /// 消费是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 消费描述
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 消费失败是否需要重新把消息放回队列
        /// </summary>
        public bool IsRequeue { get; set; }

        public SubscribeResult(bool isSuccess, bool isRequeue, string message = "")
        {
            IsSuccess = isSuccess;
            IsRequeue = isRequeue;
            Message = message;
        }
    }
}
