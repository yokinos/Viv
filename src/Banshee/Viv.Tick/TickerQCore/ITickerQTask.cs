using System;
using System.Collections.Generic;
using System.Text;
using TickerQ.Utilities.Base;

namespace Viv.Tick.TickerQCore
{
    /// <summary>
    /// TickeQ任务接口
    /// 虽然TickerQ提供了很多方式来实现任务的调度，但为了统一写法，我们还是定义一个IVivTickerQTask接口
    /// </summary>
    public interface ITickerQTask
    {
        Task<ExecuteResult> ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default);
    }
}