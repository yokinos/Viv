
using Microsoft.EntityFrameworkCore;

using System;
using System.Data;
using System.Data.SqlClient; // 新增：MsSql 连接
using Viv.Momo.Enums;
using Viv.Momo.Options;
using Viv.Vva.Magic;

namespace Viv.Momo.Contexts
{
    /// <summary>
    /// 支持多数据库+读写分离的 EF Core 上下文
    /// </summary>
    public class EFCoreContext : DbContext
    {
        private readonly DatabaseOptions _options;

        public EFCoreContext(DatabaseOptions options)
        {
            _options = options;
        }

        /// <summary>
        /// 获取读写分离后的连接字符串
        /// </summary>
        /// <param name="isRead">是否为读操作</param>
        /// <returns>适配的连接字符串</returns>
        /// <exception cref="InvalidOperationException">连接字符串配置异常</exception>
        public string GetConnectionString(bool isRead)
        {
            if (_options.ConnectionStrings == null || _options.ConnectionStrings.Length == 0)
            {
                throw new InvalidOperationException("数据库连接字符串未配置");
            }

            // 无读写分离，直接使用第一个连接
            if (!_options.IsReadWriteSplit)
            {
                return _options.ConnectionStrings[0];
            }

            if (!isRead)
            {
                return _options.ConnectionStrings[0];
            }

            // 读操作：从库列表随机选择（修复随机数边界问题）
            if (_options.ConnectionStrings.Length <= 1)
            {
                // 无从库，降级使用主库
                return _options.ConnectionStrings[0];
            }

            var readIndex = RandomMagic.Next(1, _options.ConnectionStrings.Length);
            return _options.ConnectionStrings[readIndex];
        }

        /// <summary>
        /// 配置 EF Core 数据库驱动（核心：关联读写分离连接）
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // 已配置则跳过（避免重复配置）
            if (optionsBuilder.IsConfigured) return;

            var connectionString = GetConnectionString(false);

            switch (_options.DatabaseSouce)
            {
                case DatabaseSouceType.PostgreSQL:
                    optionsBuilder.UseNpgsql(connectionString);
                    break;
                case DatabaseSouceType.MsSql:
                    optionsBuilder.UseSqlServer(connectionString);
                    break;
                case DatabaseSouceType.Sqlite:
                    optionsBuilder.UseSqlite(connectionString);
                    break;
                default:
                    // 修复：仅在不匹配时抛出异常
                    throw new NotSupportedException($"不支持的数据库类型：{_options.DatabaseSouce}");
            }
        }

        /// <summary>
        /// 暴露连接供 Dapper 使用（支持读写分离）
        /// </summary>
        /// <param name="isRead">是否为读操作（默认 true，优先走从库）</param>
        /// <returns>数据库连接</returns>
        public IDbConnection GetDbConnectionx(bool isRead = true)
        {
            // 获取适配读写分离的连接字符串
            var currentConnStr = GetConnectionString(isRead);

            var conn = _options.DatabaseSouce switch
            {
                DatabaseSouceType.PostgreSQL => new NpgsqlConnection(currentConnStr),
                DatabaseSouceType.MsSql => new SqlConnection(currentConnStr),
                DatabaseSouceType.Sqlite => new SqliteConnection(currentConnStr), // 修复：使用 EF Core 推荐的 SqliteConnection
                _ => throw new NotSupportedException($"不支持的数据库类型：{_options.DatabaseSouce}")
            };

            var connection =  Database.getd

            // 仅在连接未打开时打开（避免重复打开）
            if (conn.State != ConnectionState.Open)
            {
                conn.Open();
            }

            // 注意：连接由调用方释放（使用 using 语句）
            return conn;
        }

        /// <summary>
        /// 手动切换连接（用于强制指定读写库）
        /// </summary>
        /// <param name="isRead">是否为读操作</param>
        public void SwitchConnection(bool isRead)
        {
            _connStr = GetConnectionString(isRead);
            Database.GetDbConnection().ConnectionString = _connStr;
        }

        /// <summary>
        /// 释放资源（避免连接泄露）
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();
            // 关闭 EF Core 底层连接
            Database.GetDbConnection()?.Close();
        }

        /// <summary>
        /// 异步释放资源
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Database.GetDbConnection()?.State == ConnectionState.Open)
            {
                await Database.GetDbConnection().CloseAsync();
            }
        }
    }
}