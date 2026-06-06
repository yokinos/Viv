using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;

namespace Viv.Nana
{
    public interface IVivPublisher
    {
        /// <summary>
        /// 发布普通消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <returns></returns>
        Task<bool> PublishAsync<T>(T content) where T : NanaEvent;

        /// <summary>
        /// 发布延迟消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <returns></returns>
        Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content) where T : NanaEvent;
    }
}
