using Microsoft.EntityFrameworkCore;
using System;
using System.Data;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Options;

namespace Viv.Momo.Core
{
    /// <summary>
    /// EF Core 上下文（读写分离：初始化时确定读/写库 不允许运行时切换）
    /// </summary>
    public class EFAppContext : DbContext
    {
        /// <summary>
        /// 当前数据库连接配置
        /// </summary>
        private readonly DatabaseOptions _options;

        /// <summary>
        /// 读/写 类型
        /// </summary>
        private readonly DbReadWriteType _dbReadWriteType;

        /// <summary>
        /// 当前租户访问器（单例，静态 AsyncLocal）。用于 ITenant 实体的全局查询过滤。
        /// </summary>
        private readonly IVivContextAccessor? _tenantAccessor;

        /// <summary>
        /// EF 命令耗时的拦截器。没传 QueryTelemetry 时为 null（测试直建上下文），此时不挂、不采集
        /// </summary>
        private readonly MomoMetricsCommandInterceptor? _interceptor;

        /// <summary>
        /// 有没有租户访问器。租户过滤器用它决定挂不挂，VivLocator 未初始化（测试直建上下文）时没有
        /// </summary>
        public bool HasTenantAccessor => _tenantAccessor != null;

        /// <summary>
        /// 当前租户
        /// 过滤器表达式经 DataFilterExpression 读这里，EF 会把本次查询的上下文补进来、每次查询重求值。
        /// 不能改成把访问器当常量捕获，那样取到的是建模型那一刻的值，之后冻在缓存计划里
        /// </summary>
        public long CurrentTenantId => _tenantAccessor?.Current?.SubjectId ?? 0;

        /// <summary>
        /// 当前是否没有租户上下文。无上下文（后台消费者未设置租户）时租户过滤放行，保持既有行为。
        /// </summary>
        public bool HasNoTenantContext => _tenantAccessor?.Current == null;

        /// <summary>
        /// 某条过滤器有没有被 IDataFilter 关掉。走上下文实例是为了让 EF 按查询重新求值。
        /// </summary>
        public bool IsDataFilterDisabled(Type filterType) => DataFilterSwitch.IsDisabled(filterType);

        public EFAppContext(DatabaseOptions options, DbReadWriteType dbReadWriteType = DbReadWriteType.Read, QueryTelemetry? telemetry = null)
            : this(options, ResolveTenantAccessor(), dbReadWriteType, telemetry)
        {
        }

        public EFAppContext(DatabaseOptions options, IVivContextAccessor? tenantAccessor, DbReadWriteType dbReadWriteType = DbReadWriteType.Read, QueryTelemetry? telemetry = null)
        {
            _options = options;
            _tenantAccessor = tenantAccessor;
            _interceptor = telemetry is null ? null : new MomoMetricsCommandInterceptor(telemetry);

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
                    // SQL Server 保持 CLR PascalCase（[AtUser]/[TenantId]），与 SqlMagic / SchemaSynchronizer 一致。
                    // 不要在这边再套 UseSnakeCaseNamingConvention —— 那会建成 at_user，而 Dapper 还在打 [AtUser]。
                    optionsBuilder.UseSqlServer(connectionString, x => x.EnableRetryOnFailure())
                        .UseQueryTrackingBehavior(queryTrackingBehavior);
                    break;
                default:
                    throw new NotSupportedException($"不支持的数据库类型：{_options.DatabaseSource}");
            }

            // 拦截器与方言无关，放在 switch 外面挂，两种库都盖到
            if (_interceptor is not null)
            {
                optionsBuilder.AddInterceptors(_interceptor);
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

                // 读隔离：每条过滤器自己判断管不管这个实体、挂不挂得上、表达式怎么写
                foreach (var filter in MomoDataFilters.All)
                {
                    if (filter.AppliesTo(type) && filter.CanApply(this))
                    {
                        entity.HasQueryFilter(filter.Name, filter.BuildExpression(this, type));
                    }
                }
            }
        }

        /// <summary>
        /// 解析当前租户访问器。VivLocator 未初始化（如单元测试直建上下文）时返回 null，跳过租户过滤
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