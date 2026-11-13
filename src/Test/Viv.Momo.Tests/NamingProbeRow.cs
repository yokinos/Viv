using System.ComponentModel.DataAnnotations.Schema;
using Viv.Momo.Interface;

namespace Viv.Momo.Tests;

/// <summary>
/// 集成测试实体。刻意不以 Entity 结尾，避免进 TenantFilterTests 的 EF 扫描。
/// CLR 名带多个词，PG 上应落到 naming_probe_row / display_name。
/// </summary>
public class NamingProbeRow : IEntity
{
    /// <summary>
    /// 测试显式给 Id 插入，所以必须声明「数据库不生成」——
    /// 与 <c>EntityBase.Id</c> 一致，否则 SQL Server 上 EnsureCreated 会建出 IDENTITY，
    /// 插入显式值直接报 IDENTITY_INSERT is OFF。
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public string DisplayName { get; set; } = "";
    public int ItemCount { get; set; }
}
