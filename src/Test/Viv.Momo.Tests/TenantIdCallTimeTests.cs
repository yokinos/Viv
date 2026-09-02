using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion;
using Viv.Log;
using Viv.Momo.Core;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>
/// Dapper 路径 TenantId 必须调用时从 IVivContext 读取，不能在构造时冻结。
/// </summary>
public class TenantIdCallTimeTests
{
    [Fact]
    public void TenantId_构造后再SetSnapshot_读到新租户()
    {
        VivConfigRegistry.Add(new DatabaseOptions { Timeout = 30, MasterConnectionString = "x" });
        try
        {
            var ctx = new MutableVivContext();
            var db = new MomoDatabase(ctx, new NullLogger());

            Assert.Equal(0, db.TenantId);

            ctx.SetSnapshot(new VivContextContent { SubjectId = 77 });
            Assert.Equal(77, db.TenantId);
        }
        finally
        {
            VivConfigRegistry.Remove<DatabaseOptions>();
        }
    }

    [Fact]
    public void ChangeTenant_覆盖本实例不影响后续上下文读取()
    {
        VivConfigRegistry.Add(new DatabaseOptions { Timeout = 30, MasterConnectionString = "x" });
        try
        {
            var ctx = new MutableVivContext();
            ctx.SetSnapshot(new VivContextContent { SubjectId = 11 });
            var db = new MomoDatabaseContext(ctx, new NullLogger());

            db.ChangeTenant(99);
            Assert.Equal(99, db.TenantId);

            ctx.SetSnapshot(new VivContextContent { SubjectId = 22 });
            Assert.Equal(99, db.TenantId);
        }
        finally
        {
            VivConfigRegistry.Remove<DatabaseOptions>();
        }
    }

    private sealed class MutableVivContext : IVivContext
    {
        private VivContextContent? _snapshot;

        public long AppId => _snapshot?.AppId ?? 0;
        public long SubjectId => _snapshot?.SubjectId ?? 0;
        public long UserId => _snapshot?.UserId ?? 0;
        public string TraceId => _snapshot?.TraceId ?? "";

        public void SetSnapshot(VivContextContent model) => _snapshot = model;
        public void Clear() => _snapshot = null;
        public VivContextContent? GetRawSnapshot() => _snapshot;
    }

    private sealed class NullLogger : ILoggerContract
    {
        public void Info(string message, params object[] args) { }
        public void Error(string message, Exception ex, params object[] args) { }
        public void Error(string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Fatal(string message, params object[] args) { }
        public void Fatal(string message, Exception ex, params object[] args) { }
    }
}
