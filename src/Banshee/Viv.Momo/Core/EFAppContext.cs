using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Data;
using System.Linq.Expressions;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Core
{
    /// <summary>
    /// EF Core 上下文（读写分离：初始化时确定读/写库 不允许运行时切换）
    /// </summary>
    public class EFAppContext : DbContext
    {
        private readonly DatabaseOptions _options;
        private readonly DbReadWriteType _dbReadWriteType;

        /// <summary>
        /// 当前租户访问器（单例，静态 AsyncLocal）。用于 ITenant 实体的全局查询过滤。
        /// 模型缓存后仍按当前线程读取租户，因此跨请求正确。
        /// </summary>
        private readonly IVivContextAccessor? _tenantAccessor;

        public EFAppContext(DatabaseOptions options, DbReadWriteType dbReadWriteType = DbReadWriteType.Read)
            : this(options, ResolveTenantAccessor(), dbReadWriteType)
        {
        }

        public EFAppContext(DatabaseOptions options, IVivContextAccessor? tenantAccessor, DbReadWriteType dbReadWriteType = DbReadWriteType.Read)
        {
            _options = options;
            _tenantAccessor = tenantAccessor;

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
        public string GetConnectionString()
        {
            if (_options.MasterConnectionString.IsNullOrEmpty())
            {
                throw new InvalidOperationException("未配置主库连接字符串");
            }

            // 无读写分离/读操作/无从库 → 使用主库
            if (!_options.IsReadWriteSplit || _dbReadWriteType == DbReadWriteType.Write || _options.SlaveConnectionStrings.IsNullOrEmpty())
            {
                return _options.MasterConnectionString;
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

            var connectionString = GetConnectionString();
            var queryTrackingBehavior = _dbReadWriteType == DbReadWriteType.Read ? QueryTrackingBehavior.NoTracking : QueryTrackingBehavior.TrackAll;

            switch (_options.DatabaseSource)
            {
                case DatabaseSourceType.PostgreSQL:
                    optionsBuilder.UseNpgsql(connectionString, x => x.EnableRetryOnFailure())
                        .UseQueryTrackingBehavior(queryTrackingBehavior)
                        .UseSnakeCaseNamingConvention();
                    break;
                case DatabaseSourceType.SqlServer:
                    optionsBuilder.UseSqlServer(connectionString, x => x.EnableRetryOnFailure())
                        .UseQueryTrackingBehavior(queryTrackingBehavior)
                        .UseSnakeCaseNamingConvention();
                    break;
                default:
                    throw new NotSupportedException($"不支持的数据库类型：{_options.DatabaseSource}");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            if (_options.EntityTypeOptions.IsNullOrEmpty())
            {
                return;
            }

            var typeList = TypeScanMagic.ScanRange(_options.EntityTypeOptions);
            foreach (var type in typeList)
            {
                var entity = modelBuilder.Entity(type);

                // 多租户隔离：ITenant 实体全局查询过滤，阻止租户读取/查询到其他租户的行
                if (_tenantAccessor != null && typeof(ITenant).IsAssignableFrom(type))
                {
                    ApplyTenantFilter(entity, type);
                }
            }
        }

        /// <summary>
        /// 解析当前租户访问器。VivLocator 未初始化（如单元测试直建上下文）时返回 null，跳过租户过滤。
        /// </summary>
        private static IVivContextAccessor? ResolveTenantAccessor()
        {
            try
            {
                return VivLocator.GetService<IVivContextAccessor>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 对 ITenant 实体加全局查询过滤：e => 无请求上下文 || e.TenantId == 当前租户。
        /// 无上下文（后台消费者未设置租户）时不过滤，保持既有行为；HTTP 请求路径由
        /// VivContextMiddleware 保证必有上下文，因此请求侧跨租户读取被拦截。
        /// 表达式捕获 accessor 常量（单例），EF 每次查询重新求值 Current.SubjectId。
        /// </summary>
        private void ApplyTenantFilter(EntityTypeBuilder entity, Type type)
        {
            var e = Expression.Parameter(type, "e");
            var accessor = Expression.Constant(_tenantAccessor);
            var current = Expression.Property(accessor, nameof(IVivContextAccessor.Current));
            var subjectId = Expression.Property(current, nameof(VivContextContent.SubjectId));
            var hasContext = Expression.NotEqual(current, Expression.Constant(null, typeof(VivContextContent)));

            var body = Expression.OrElse(
                Expression.Not(hasContext),
                Expression.Equal(Expression.PropertyOrField(e, nameof(ITenant.TenantId)), subjectId));

            entity.HasQueryFilter(Expression.Lambda(body, e));
        }

        /// <summary>
        /// 暴露连接供 Dapper 使用（返回当前上下文绑定的读/写库连接）
        /// </summary>
        public IDbConnection DbConnection
        {
            get
            {
                var connection = Database.GetDbConnection();

                // 确保连接打开 Dapper会自己处理
                //if (connection.State != ConnectionState.Open)
                //{
                //    connection.Open();
                //}

                return connection;
            }
        }
    }
}