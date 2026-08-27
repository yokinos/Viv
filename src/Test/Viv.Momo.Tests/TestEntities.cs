using System.ComponentModel.DataAnnotations.Schema;
using Viv.Momo.Interface;

namespace Viv.Momo.Tests;

/// <summary>租户实体：ITenant + IEntity</summary>
public class TenantUserEntity : IEntity, ITenant
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

/// <summary>租户软删除实体</summary>
public class SoftDeleteTenantEntity : IEntity, ITenant, ISoftDeleted
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>非租户实体：无 ITenant</summary>
public class NonTenantEntity : IEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>无接口平面实体：属性声明序固定，用于 SQL 模板断言</summary>
public class FlatRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

/// <summary>含可空属性，用于 null 跳过测试</summary>
public class NullableRow
{
    public long Id { get; set; }
    public string? Name { get; set; }
}

/// <summary>带 [Table] 特性，测试表名读取</summary>
[Table("sys_users")]
public class TableNamedEntity
{
    public long Id { get; set; }
}

/// <summary>枚举字面量转换测试</summary>
public enum TestState
{
    None = 0,
    Active = 2
}
