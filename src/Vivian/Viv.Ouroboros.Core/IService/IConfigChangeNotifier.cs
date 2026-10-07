using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Ouroboros.Core.IService
{
    /// <summary>
    /// 配置写操作后的失效通知：本进程立刻清缓存，并把共享版本戳广播出去，
    /// 让其它实例在节流窗口内自行清缓存（无 Redis 时退化为只清本进程）。
    /// </summary>
    public interface IConfigChangeNotifier
    {
        /// <summary>
        /// 通知"配置变了"
        /// </summary>
        /// <param name="agentKey">改动的 Agent 业务键；为空表示范围说不准，Agent 与工具缓存全清</param>
        /// <param name="profileKey">改动的档位键</param>
        void Notify(string? agentKey = null, string? profileKey = null);
    }
}
