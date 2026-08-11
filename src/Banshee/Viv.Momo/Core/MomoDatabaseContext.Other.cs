using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Threading.Tasks;
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Momo.Sync;

namespace Viv.Momo.Core
{
    public partial class MomoDatabaseContext
    {
        #region Other

        public IMomoDbContext? CreateContext(DatabaseOptions options)
        {
            if (options == null) return null;
            var dataContext = new MomoDatabaseContext(_vivContext, _logger);
            dataContext.SetOptions(options);
            return dataContext;
        }

        public void ChangeTenant(long tenantId)
        {
            if (tenantId > 0)
                TenantId = tenantId;
        }

        public void IsAutoSetDefaultValue(bool flag)
        {
            IsAutoSetValue = flag;
        }

        public EFAppContext GetEFContext(DbReadWriteType readWriteType)
        {
            return GetAppContext(readWriteType);
        }

        public IDbConnection GetDbConnection(DbReadWriteType readWriteType = DbReadWriteType.Read)
        {
            return GetAppContext(readWriteType).DbConnection;
        }

        public async Task SyncTableAsync(bool allowDrop = false, CancellationToken cancellationToken = default)
        {
            var context = GetAppContext(DbReadWriteType.Write);

            // 1. EF Core EnsureCreated：创建数据库中不存在的表
            await context.Database.EnsureCreatedAsync(cancellationToken);

            // 2. SchemaSynchronizer：处理列级变更
            var sync = new SchemaSynchronizer(_options);
            var entityTypes = sync.ScanEntityTypes();
            if (entityTypes.Count == 0)
                return;

            var expected = sync.BuildExpectedSchema(entityTypes);
            var actual = await sync.FetchActualSchemaAsync(cancellationToken);
            var diff = sync.Diff(expected, actual);

            if (!diff.HasChanges)
                return;

            // 默认禁止 DROP，避免改属性名时误删数据
            if (!allowDrop)
            {
                if (diff.DeletedTables.Count > 0)
                {
                    WriteLog($"SyncTable: skip DROP {diff.DeletedTables.Count} table(s): {string.Join(", ", diff.DeletedTables.Select(t => t.TableName))}", null!);
                    diff.DeletedTables.Clear();
                }
                foreach (var table in diff.ModifiedTables)
                {
                    var drops = table.ColumnDiffs.Where(c => c.Type == DiffType.Deleted).ToList();
                    if (drops.Count > 0)
                    {
                        WriteLog($"SyncTable: skip DROP {drops.Count} column(s) in [{table.TableName}]: {string.Join(", ", drops.Select(c => c.ColumnName))}", null!);
                        table.ColumnDiffs.RemoveAll(c => c.Type == DiffType.Deleted);
                    }
                }
                diff.ModifiedTables.RemoveAll(t => t.ColumnDiffs.Count == 0);
            }

            if (diff.HasChanges)
            {
                var ddl = sync.GenerateDdl(diff);
                foreach (var sql in ddl)
                    await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
        }

        #endregion
    }
}
