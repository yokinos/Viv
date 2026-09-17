using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo.Core;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Fakes;

/// <summary>
/// 暴露 <see cref="MomoDatabase"/> 受保护成员的探针 —— 审计字段的填充分支全在 <c>protected</c> 方法里。
///
/// 这些方法不碰数据库（只改实体属性 + 读 <see cref="IVivContext"/>），所以构造一个不需要真库的
/// 实例就能测。真正的落库路径（<c>SetValues</c> + <c>SaveChanges</c>）在 CI 里没有库可跑，
/// 这个探针覆盖不到，别拿它冒充。
/// </summary>
public class MomoAuditSut : MomoDatabase
{
    public MomoAuditSut(IVivContext context)
        : base(
            context,
            new RecordingLogger(),
            new DefaultDatabaseOptionsProvider(XUnitTestMagic.CreateOptions(new DatabaseOptions())))
    {
    }

    /// <summary>被测的 <c>CurrentUserId</c> —— 「无登录上下文记 null 而不是 0」那条断言的入口</summary>
    public long? CurrentUserIdForTest => CurrentUserId;

    /// <summary>关掉自动填充开关（<c>IsAutoSetValue</c> 是 <c>protected set</c>，getter 本来就 public）</summary>
    public void DisableAutoSetValue() => IsAutoSetValue = false;

    public void AutoSetInsert<T>(params T[] entities) where T : IEntity => AutoSetInsertValue(entities);

    public void AutoSetUpdate<T>(params T[] entities) where T : IEntity => AutoSetUpdateValue(entities);

    /// <summary>
    /// <c>Update</c> 路径在 <c>SetValues</c> 之前调的保护逻辑。
    /// 单条 Update 的三行编排（补回 → 盖章 → SetValues）需要真库才跑得到，这里只钉死它依赖的那个纯函数。
    /// </summary>
    public static void PreserveProtectedValues(IEntity from, IEntity to) => CopyProtectedValues(from, to);
}
