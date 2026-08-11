using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo.Interface;

namespace Viv.Momo.Core
{
    /// <summary>
    /// Viv 框架下的数据库访问实现（基于 EFCore 与 Dapper，支持 PostgreSQL、SqlServer）
    /// 按职责拆分为多个 partial 文件：
    ///   MomoDatabaseContext.Insert.cs     — 新增
    ///   MomoDatabaseContext.Update.cs     — 修改（含批量）
    ///   MomoDatabaseContext.Delete.cs     — 删除 + 软删除
    ///   MomoDatabaseContext.Query.cs      — 查询 + 分页
    ///   MomoDatabaseContext.ExecuteSql.cs — 原生 SQL 执行（含批量事务）
    ///   MomoDatabaseContext.Other.cs      — 其他（上下文/租户/表同步）
    /// </summary>
    public partial class MomoDatabaseContext : MomoDatabase, IMomoDbContext
    {
        private bool _disposed;

        public MomoDatabaseContext(IVivContext vivContext, ILoggerContract logger)
            : base(vivContext, logger) { }

        #region Dispose

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {

            }

            base.Dispose(disposing);
            _disposed = true;
        }

        #endregion
    }
}
