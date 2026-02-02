using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.RabbitMQ
{
    /// <summary>
    /// 适配Viv框架的消费者基类（自动重试 降级处理 支持(RabbitMQ (推送死信队列)）
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class VivConsumer<T>
    {

    }
}
