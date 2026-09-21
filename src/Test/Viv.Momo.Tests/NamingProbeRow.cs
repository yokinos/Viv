using Viv.Momo.Interface;

namespace Viv.Momo.Tests;

/// <summary>
/// 集成测试实体。刻意不以 Entity 结尾，避免进 TenantFilterTests 的 EF 扫描。
/// CLR 名带多个词，PG 上应落到 naming_probe_row / display_name。
/// </summary>
public class NamingProbeRow : IEntity
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = "";
    public int ItemCount { get; set; }
}
