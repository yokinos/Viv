using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;

namespace Viv.Nana
{
    /// <summary>
    /// 适配Viv框架的消费者基类（自动重试 降级处理 支持(RabbitMQ (推送死信队列,并支持消费死信（提供默认实现允许重写）),支持Redis发布订阅（这玩意就没有死信了），支持最终兜底本地消息表消费）
    /// </summary>
    /// <typeparam name="T">消息模型（需要继承[VivMQMessaage]）</typeparam>
    public abstract class VivNanaConsumer<T> : NanaFactory where T : NanaMessage
    {
        
    }
}
