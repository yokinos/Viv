using Dapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Viv.Delusion.Extension;
using Viv.Delusion.Generic;
using Viv.Momo.Enums;

namespace Viv.Momo.Core
{
    public partial class MomoDatabaseContext
    {
        #region ExecuteSql

        public bool ExecuteSql(string sql, object? parameters = null)
        {
            if (string.IsNullOrEmpty(sql)) return false;

            try
            {
                var context = GetAppContext();
                var count = context.DbConnection.Execute(sql, parameters, _transaction, _timeOut);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"ExecuteSql（SQL）,{sql},{ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> ExecuteSqlAsync(string sql, object? parameters = null)
        {
            if (string.IsNullOrEmpty(sql)) return false;

            try
            {
                var context = GetAppContext();
                var count = await context.DbConnection.ExecuteAsync(sql, parameters, _transaction, _timeOut).ConfigureAwait(false);
                return count > 0;
            }
            catch (Exception ex)
            {
                WriteLog($"ExecuteSqlAsync（SQL）,{sql},{ex.Message}", ex);
                return false;
            }
        }

        public bool ExecuteSqlList(List<string> sqlList, object? parameters = null, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)context.Database.BeginTransaction();
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalPages = CalculateTotalPages(sqlList.Count, batchSize);
                for (int page = 1; page <= totalPages; page++)
                {
                    var batch = sqlList.Skip((page - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batch) + ";";
                    connection.Execute(batchSql, parameters, transaction, _timeOut);
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    context.Database.CommitTransaction();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    context.Database.RollbackTransaction();
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList);
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public async Task<bool> ExecuteSqlListAsync(List<string> sqlList, object? parameters = null, bool isTxn = true, CancellationToken cancellationToken = default)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    isSelfCreatedTxn = _transaction == null;
                }

                int batchSize = 500;
                int totalPages = CalculateTotalPages(sqlList.Count, batchSize);
                for (int page = 1; page <= totalPages; page++)
                {
                    var batch = sqlList.Skip((page - 1) * batchSize).Take(batchSize).ToList();
                    var batchSql = string.Join(";", batch) + ";";
                    await connection.ExecuteAsync(batchSql, parameters, transaction, _timeOut);
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList);
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public bool ExecuteSqlList(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction)context.Database.BeginTransaction();
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var item in sqlList)
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        connection.Execute(item.Key, item.Value, transaction, _timeOut);
                    }
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    context.Database.CommitTransaction();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    context.Database.RollbackTransaction();
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10).Select(x => x.Key))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList.Select(x => x.Key));
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        public async Task<bool> ExecuteSqlListAsync(List<KeyValueItem<string, object?>> sqlList, bool isTxn = true, CancellationToken cancellationToken = default)
        {
            if (sqlList.IsNullOrEmpty()) return false;

            EFAppContext? context = null;
            IDbTransaction? transaction = null;
            bool isSelfCreatedTxn = false;

            try
            {
                context = GetAppContext(DbReadWriteType.Write);
                var connection = context.DbConnection;

                if (isTxn)
                {
                    transaction = _transaction ?? (IDbTransaction) await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    isSelfCreatedTxn = _transaction == null;
                }

                foreach (var item in sqlList)
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        await connection.ExecuteAsync(item.Key, item.Value, transaction, _timeOut).ConfigureAwait(false);
                    }
                }

                if (isSelfCreatedTxn && transaction != null)
                {
                    await context.Database.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (isSelfCreatedTxn && context != null && transaction != null)
                {
                    await context.Database.RollbackTransactionAsync(cancellationToken).ConfigureAwait(false);
                }

                var log = sqlList.Count > 10
                    ? $"前10条SQL：{string.Join(",", sqlList.Take(10).Select(x => x.Key))}...（共{sqlList.Count}条）"
                    : string.Join(",", sqlList.Select(x => x.Key));
                WriteLog(log, ex);
                return false;
            }
            finally
            {
                if (isSelfCreatedTxn && transaction != null)
                {
                    transaction.Dispose();
                }
            }
        }

        #endregion
    }
}
