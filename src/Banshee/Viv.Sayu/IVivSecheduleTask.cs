using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Sayu
{
    /// <summary>
    /// 调度任务执行封装
    /// </summary>
    public interface IVivSecheduleTask
    {
        Task<ExecuteResult> ExecuteAsync(CancellationToken cancellationToken = default);
    }
}
