using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Sandrone.Impl
{
    /// <summary>
    /// IVivContextAccessor 实现
    /// AsyncLocal 唯一存放位置，禁止在其他类新增 AsyncLocal
    /// 注意：AsyncLocal 会随 ExecutionContext 流入 Task.Run / 新线程。请求中 fire-and-forget 的
    /// 后台任务会继承发起请求的租户上下文，请求结束后仍带着旧租户跑 → 后台跨租户。
    /// 业务代码如用 Task.Run / new Thread 做租户敏感操作，需自行 ExecutionContext.SuppressFlow()
    /// 或在任务内显式清除/重设租户上下文。
    /// </summary>
    public class VivContextAccessor : IVivContextAccessor
    {
        private static readonly AsyncLocal<VivContextContent?> _storage = new();

        public VivContextContent? Current
        {
            get => _storage.Value;
            set => _storage.Value = value;
        }
    }
}
