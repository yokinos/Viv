using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;

namespace Viv.Nana
{
    public interface IVivEventPublisher
    {
        /// <summary>
        /// 发布普通消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> PublishAsync<T>(T content, CancellationToken cancellationToken = default) where T : NanaEvent;

        /// <summary>
        /// 发布延迟消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="delayTTL"></param>
        /// <param name="content"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, T content, CancellationToken cancellationToken = default) where T : NanaEvent;

        /// <summary>
        /// 发布延迟消息（信封重投）—— 直接调度原信封，保留 MessageId/ReDeliverCount/DelaySecond/Context，
        /// 供 VivConsumer 延迟重投计数跟踪（内容版重载会新建信封，丢失这些元数据）。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="delayTTL"></param>
        /// <param name="envelope"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<bool> PublishDelayAsync<T>(TimeSpan delayTTL, NanaEnvelope<T> envelope, CancellationToken cancellationToken = default) where T : NanaEvent;
    }
}
