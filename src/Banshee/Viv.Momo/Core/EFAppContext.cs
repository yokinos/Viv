using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Data;
using System.Reflection;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;
using Viv.Vva.Magic;

namespace Viv.Momo.Core
{
    /// <summary>
    /// EF Core 上下文（读写分离：初始化时确定读/写库，避免运行时切换）
    /// </summary>
    public class EFAppContext : DbContext
    {
        private readonly DatabaseOptions _options;
        private readonly DbReadWriteType _dbReadWriteType;
        private IDbConnection _cachedConnection;

        public EFAppContext(DatabaseOptions options, DbReadWriteType dbReadWriteType = DbReadWriteType.Read)
        {
            _options = options;

            if (options.IsReadWriteSplit)
            {
                _dbReadWriteType = dbReadWriteType;
            }
            else
            {
                // 无读写分离 → 走主库
                _dbReadWriteType = DbReadWriteType.Write;
            }

            // 事务约束：读库上下文禁止开启事务
            if (_dbReadWriteType == DbReadWriteType.Read)
            {
                Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            }
        }

        /// <summary>
        /// 获取读写分离后的连接字符串（核心：初始化时确定）
        /// </summary>
        /// <param name="isRead">是否为读操作</param>
        /// <returns>适配的连接字符串</returns>
        /// <exception cref="InvalidOperationException">连接字符串配置异常</exception>
        public string GetConnectionString(DbReadWriteType dbReadWriteType)
        {
            if (_options.MasterConnectionStrings.IsNullOrEmpty())
            {
                throw new InvalidOperationException("未配置主库连接字符串");
            }

            // 无读写分离/读操作/无从库 → 使用主库
            if (!_options.IsReadWriteSplit || dbReadWriteType == DbReadWriteType.Write || _options.SlaveConnectionStrings.IsNullOrEmpty())
            {
                var masterIndex = RandomMagic.Next(0, _options.MasterConnectionStrings.Length);
                return _options.MasterConnectionStrings[masterIndex];
            }

            // 读操作 → 随机选择从库
            var readIndex = RandomMagic.Next(0, _options.SlaveConnectionStrings.Length);
            return _options.SlaveConnectionStrings[readIndex];
        }

        /// <summary>
        /// 配置 EF Core 数据库驱动（初始化时绑定读/写库）
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured) return;

            var connectionString = GetConnectionString(_dbReadWriteType);
            var queryTrackingBehavior = _dbReadWriteType == DbReadWriteType.Read ? QueryTrackingBehavior.NoTracking : QueryTrackingBehavior.TrackAll;

            switch (_options.DatabaseSouce)
            {
                case DatabaseSouceType.PostgreSQL:
                    optionsBuilder.UseNpgsql(connectionString)
                        .UseQueryTrackingBehavior(queryTrackingBehavior);
                    break;
                case DatabaseSouceType.SqlServer:
                    optionsBuilder.UseSqlServer(connectionString)
                        .UseQueryTrackingBehavior(queryTrackingBehavior);
                    break;
                default:
                    throw new NotSupportedException($"不支持的数据库类型：{_options.DatabaseSouce}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            if (_options.EntityAsseblyNames.IsNullOrEmpty())
            {
                return;
            }

            var typeList = new List<Type>();
            foreach (var assemblyName in _options.EntityAsseblyNames)
            {
                var assembly = Assembly.Load(assemblyName);
                if (assembly == null) continue;

                var entityTypes = assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IEntity).IsAssignableFrom(t));
                typeList.AddRange(entityTypes);
            }

            foreach (var type in typeList)
            {
                modelBuilder.Entity(type);
            }
        }

        /// <summary>
        /// 暴露连接供 Dapper 使用（返回当前上下文绑定的读/写库连接）
        /// </summary>
        public IDbConnection DbConnection
        {
            get
            {
                // 缓存连接，避免重复获取
                if (_cachedConnection == null)
                {
                    _cachedConnection = Database.GetDbConnection();
                }

                // 确保连接打开
                if (_cachedConnection.State != ConnectionState.Open)
                {
                    _cachedConnection.Open();
                }

                return _cachedConnection;
            }
        }
    }
}