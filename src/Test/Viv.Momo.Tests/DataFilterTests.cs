using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Magic;
using Viv.Fakes;
using Viv.Momo.Core;
using Viv.Momo.DataFilter;
using Viv.Momo.Enums;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Tests;

/// <summary>
/// 数据过滤器（IMomoDataFilter + IDataFilter 开关）的 EF 那一半。
/// 表达式直接从建好的模型里取出来编译执行，不需要数据库，所以 CI 能真跑到「放行还是挡住」这个方向。
/// </summary>
public class DataFilterTests
{
    /// <summary>过滤器名字的字面量，改名就该让这里红</summary>
    private const string SoftDeleteFilterName = "softdelete";

    private const string TenantFilterName = "tenant";

    private static DatabaseOptions EntityScanOptions()
        => new()
        {
            DatabaseSource = DatabaseSourceType.SqlServer,
            EntityTypeOptions =
            [
                new FilterTypeOptions { AssemblyName = "Viv.Momo.Tests", ClassNameEndsWith = "Entity" }
            ]
        };

    private static ModelBuilder BuildModel(IVivContextAccessor? accessor)
        => new ExposedEfAppContext(EntityScanOptions(), accessor).BuildModel();

    /// <summary>摆一个租户 7 的请求上下文。租户过滤每次查询重求值，所以建完模型再摆也照样生效</summary>
    private static TestContextAccessor TenantAccessor(long subjectId)
        => new() { Current = new VivContextContent { SubjectId = subjectId } };

    private static List<string?> QueryFilterKeys(ModelBuilder model, Type entityType)
        => model.Entity(entityType).Metadata.GetDeclaredQueryFilters().Select(x => x.Key).ToList();

    /// <summary>把模型里某一条过滤器取出来编译成可执行的委托</summary>
    private static Func<TEntity, bool> CompileFilter<TEntity>(ModelBuilder model, Type entityType, string filterName)
    {
        var filter = model.Entity(entityType).Metadata
            .GetDeclaredQueryFilters()
            .Single(x => x.Key == filterName);

        var lambda = filter.Expression as LambdaExpression
            ?? throw new InvalidOperationException($"过滤器 {filterName} 的表达式不是 lambda 表达式");

        return (Func<TEntity, bool>)lambda.Compile();
    }

    #region 软删除过滤

    [Fact]
    public void 软删除过滤_未删除行放行_已删除行挡住()
    {
        var filter = CompileFilter<SoftDeleteTenantEntity>(BuildModel(new TestContextAccessor()), typeof(SoftDeleteTenantEntity), SoftDeleteFilterName);

        Assert.True(filter(new SoftDeleteTenantEntity { Id = 1, Name = "活着", IsDeleted = false }));
        Assert.False(filter(new SoftDeleteTenantEntity { Id = 2, Name = "已删", IsDeleted = true }));
    }

    [Fact]
    public void 软删除过滤_关掉后已删除行也放行_作用域结束即恢复()
    {
        var filter = CompileFilter<SoftDeleteTenantEntity>(BuildModel(new TestContextAccessor()), typeof(SoftDeleteTenantEntity), SoftDeleteFilterName);

        using (new DataFilterSwitch().Disable<SoftDeletedFilter>())
        {
            Assert.True(filter(new SoftDeleteTenantEntity { IsDeleted = true }));
        }

        // 句柄释放后回到过滤态
        Assert.False(filter(new SoftDeleteTenantEntity { IsDeleted = true }));
    }

    #endregion

    #region 租户过滤

    [Fact]
    public void 租户过滤_同租户放行_他租户挡住()
    {
        var filter = CompileFilter<TenantUserEntity>(BuildModel(TenantAccessor(7)), typeof(TenantUserEntity), TenantFilterName);

        Assert.True(filter(new TenantUserEntity { TenantId = 7 }));
        Assert.False(filter(new TenantUserEntity { TenantId = 9 }));
    }

    [Fact]
    public void 租户过滤_关掉后他租户也放行_作用域结束即恢复()
    {
        var filter = CompileFilter<TenantUserEntity>(BuildModel(TenantAccessor(7)), typeof(TenantUserEntity), TenantFilterName);

        using (new DataFilterSwitch().Disable<TenantDataFilter>())
        {
            Assert.True(filter(new TenantUserEntity { TenantId = 9 }));
        }

        Assert.False(filter(new TenantUserEntity { TenantId = 9 }));
    }

    [Fact]
    public void 关掉一个过滤器不影响另一个()
    {
        var model = BuildModel(TenantAccessor(7));
        var softDelete = CompileFilter<SoftDeleteTenantEntity>(model, typeof(SoftDeleteTenantEntity), SoftDeleteFilterName);
        var tenant = CompileFilter<SoftDeleteTenantEntity>(model, typeof(SoftDeleteTenantEntity), TenantFilterName);

        using (new DataFilterSwitch().Disable<SoftDeletedFilter>())
        {
            // 软删除放行，租户照旧挡着，两条开关各管各的
            Assert.True(softDelete(new SoftDeleteTenantEntity { IsDeleted = true, TenantId = 7 }));
            Assert.False(tenant(new SoftDeleteTenantEntity { IsDeleted = false, TenantId = 9 }));
        }
    }

    #endregion

    #region 过滤器装配

    [Fact]
    public void 具名过滤器_租户与软删除两条并存()
    {
        var model = BuildModel(TenantAccessor(7));
        var keys = QueryFilterKeys(model, typeof(SoftDeleteTenantEntity));

        // HasQueryFilter 的单参重载是替换语义，这里两条都在才说明用的是具名重载
        Assert.Contains(TenantFilterName, keys);
        Assert.Contains(SoftDeleteFilterName, keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void 无租户访问器_租户过滤缺席_软删除过滤照旧()
    {
        var model = BuildModel(null);
        var keys = QueryFilterKeys(model, typeof(SoftDeleteTenantEntity));

        Assert.DoesNotContain(TenantFilterName, keys);
        Assert.Contains(SoftDeleteFilterName, keys);

        // 软删除不依赖访问器，无上下文一样挡得住
        Assert.False(CompileFilter<SoftDeleteTenantEntity>(model, typeof(SoftDeleteTenantEntity), SoftDeleteFilterName)
            (new SoftDeleteTenantEntity { IsDeleted = true }));
    }

    [Fact]
    public void 非软删除实体_不加软删除过滤()
    {
        var model = BuildModel(TenantAccessor(7));

        Assert.Empty(QueryFilterKeys(model, typeof(NonTenantEntity)));
    }

    #endregion

    #region EF 真翻译（不连库）

    /// <summary>
    /// 真上下文，只为让 provider 把查询编译成 SQL。连接串是假的，ToQueryString 不开连接。
    /// </summary>
    private static EFAppContext BuildContext(IVivContextAccessor? accessor)
    {
        var options = EntityScanOptions();
        options.MasterConnectionString = "Server=localhost;Database=viv_test;Trusted_Connection=True;TrustServerCertificate=True";
        return new EFAppContext(options, accessor, DbReadWriteType.Write);
    }

    /// <summary>EF 给查询过滤器生成的参数前缀，形如 @ef_filter__CurrentTenantId</summary>
    private const string EfFilterParameterPrefix = "ef_filter__";

    /// <summary>
    /// EF 为过滤器生成的参数。断言参数值而不是「SQL 里出现过 IsDeleted / TenantId」——
    /// 那两个列在 SELECT 投影里本来就有，过滤器整条没了照样命中。
    /// </summary>
    private static Dictionary<string, object?> FilterParameters(IQueryable queryable)
    {
        using var command = queryable.CreateDbCommand();
        return command.Parameters.Cast<DbParameter>()
            .Where(x => x.ParameterName.Contains("filter"))
            .ToDictionary(x => x.ParameterName, x => x.Value);
    }

    private static object? FilterParameter(IQueryable queryable, string nameFragment)
        => FilterParameters(queryable).Single(x => x.Key.Contains(nameFragment)).Value;

    /// <summary>
    /// 租户过滤的取值要跟着上下文走。
    /// 回归测试：过滤器原先捕获 accessor 常量，被 EF 烘进缓存的查询计划，
    /// 同一查询形状第一次编译时取的租户值会一直用下去，换请求换租户都不变。
    /// </summary>
    [Fact]
    public void 租户过滤_参数值随上下文走_不同租户各自生效()
    {
        using var seven = BuildContext(TenantAccessor(7));
        using var nine = BuildContext(TenantAccessor(9));

        // 两个上下文查同一个实体、SQL 形状完全相同，正是当初会串租户的场景
        Assert.Equal(7L, FilterParameter(seven.Set<TenantUserEntity>(), "CurrentTenantId"));
        Assert.Equal(9L, FilterParameter(nine.Set<TenantUserEntity>(), "CurrentTenantId"));
    }

    /// <summary>
    /// 同一个上下文、同一个查询形状，开关能反复切，也是被冻过的地方。
    /// 不断言参数叫什么名字（EF 按表达式推导，方法调用会取成 pN），只断言翻动的恰好只有开关那一个。
    /// </summary>
    [Fact]
    public void 软删除过滤_开关在同一个查询形状上反复切换都生效()
    {
        using var context = BuildContext(new TestContextAccessor());
        IQueryable<SoftDeleteTenantEntity> Query() => context.Set<SoftDeleteTenantEntity>();

        var before = FilterParameters(Query());

        using (new DataFilterSwitch().Disable<SoftDeletedFilter>())
        {
            var during = FilterParameters(Query());
            Assert.Equal(before.Count, during.Count);

            // 软删除那条的放行位翻成 true，租户那几条不动
            var changed = during.Where(x => !Equals(x.Value, before[x.Key])).ToList();
            Assert.Equal(true, Assert.Single(changed).Value);
        }

        // 句柄释放后回到进入之前，同一个查询形状上再切一次
        Assert.Equal(before, FilterParameters(Query()));
    }

    /// <summary>
    /// 过滤器表达式要能被 EF 翻译，译不出来是查询期抛异常，编译期没提示。
    /// </summary>
    [Fact]
    public void 过滤器_进得去SQL的WHERE而不是只出现在投影里()
    {
        using var context = BuildContext(TenantAccessor(7));

        var sql = context.Set<TenantUserEntity>().ToQueryString();

        // 断言「SQL 里出现过 TenantId」没用，那一列在 SELECT 投影里本来就有。
        // 要断言的是它带着过滤器参数落在 WHERE 里。
        var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        Assert.True(whereIndex > 0, sql);

        var where = sql[whereIndex..];
        Assert.Contains("TenantId", where, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EfFilterParameterPrefix, where, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 开关自身

    [Fact]
    public void DataFilter_嵌套关掉_逐层恢复且释放幂等()
    {
        IDataFilter filter = new DataFilterSwitch();
        Assert.False(filter.IsDisabled<SoftDeletedFilter>());
        Assert.False(filter.IsDisabled<TenantDataFilter>());

        var outer = filter.Disable<SoftDeletedFilter>();
        Assert.True(filter.IsDisabled<SoftDeletedFilter>());

        var inner = filter.Disable<TenantDataFilter>();
        Assert.True(filter.IsDisabled<SoftDeletedFilter>());
        Assert.True(filter.IsDisabled<TenantDataFilter>());

        inner.Dispose();
        Assert.True(filter.IsDisabled<SoftDeletedFilter>());
        Assert.False(filter.IsDisabled<TenantDataFilter>());

        outer.Dispose();
        Assert.False(filter.IsDisabled<SoftDeletedFilter>());

        // 重复释放不改变状态
        outer.Dispose();
        Assert.False(filter.IsDisabled<SoftDeletedFilter>());
    }

    [Fact]
    public void DataFilterScope_一次关掉多个_释放时一起恢复()
    {
        IDataFilter filter = new DataFilterSwitch();

        using (var scope = filter.Scope())
        {
            scope.Disable<SoftDeletedFilter>()
                .Disable<TenantDataFilter>();

            Assert.True(filter.IsDisabled<SoftDeletedFilter>());
            Assert.True(filter.IsDisabled<TenantDataFilter>());
        }

        Assert.False(filter.IsDisabled<SoftDeletedFilter>());
        Assert.False(filter.IsDisabled<TenantDataFilter>());
    }

    [Fact]
    public void DataFilterScope_按类型关_与泛型同一份状态()
    {
        IDataFilter filter = new DataFilterSwitch();

        using (var scope = filter.Scope())
        {
            scope.Disable(typeof(SoftDeletedFilter));
            Assert.True(filter.IsDisabled<SoftDeletedFilter>());
        }

        Assert.False(filter.IsDisabled<SoftDeletedFilter>());
    }

    [Fact]
    public void DataFilterScope_嵌在既有句柄之上_只恢复自己那层()
    {
        IDataFilter filter = new DataFilterSwitch();

        using (filter.Disable<SoftDeletedFilter>())
        {
            using (var scope = filter.Scope())
            {
                scope.Disable<TenantDataFilter>();
                Assert.True(filter.IsDisabled<TenantDataFilter>());
            }

            // 作用域释放只回退它关的那条，外面那条还关着
            Assert.False(filter.IsDisabled<TenantDataFilter>());
            Assert.True(filter.IsDisabled<SoftDeletedFilter>());
        }

        Assert.False(filter.IsDisabled<SoftDeletedFilter>());
    }

    [Fact]
    public void DataFilterScope_释放后再关_直接抛()
    {
        IDataFilter filter = new DataFilterSwitch();
        var scope = filter.Scope();
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { scope.Disable<SoftDeletedFilter>(); });

        // 释放后的作用域不该把状态改回去
        Assert.False(filter.IsDisabled<SoftDeletedFilter>());
    }

    #endregion
}
