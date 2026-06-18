using System;
using System.Collections.Generic;
using System.Text;
using TickerQ.Utilities.Base;
using Viv.Delusion;

namespace Viv.Tick.TickerQCore
{
    /// <summary>
    /// TickeQ任务接口
    /// </summary>
    public interface ITickerQTask
    {
        Task<FuncResult> ExecuteAsync(TickerFunctionContext context, CancellationToken cancellationToken = default);
    }
}