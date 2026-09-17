namespace Viv.Momo.Interface
{
    /// <summary>
    /// 创建人审计能力 —— 新增时由 <c>MomoDatabase</c> 自动盖 <c>IVivContext.UserId</c>。
    ///
    /// <para>
    /// ⚠️ 取的是 <c>UserId</c>（当前登录用户），<b>不是</b> <c>SubjectId</c>（租户 / 组织 / 公司主体，
    /// <c>MomoDatabase.TenantId</c> 取的那个）。无登录上下文时（Worker / 消息消费 / 后台任务）
    /// <c>UserId == 0</c>，此时记 <c>null</c> 而不是 <c>0</c> —— 否则「谁创建的」里混进一堆 0，
    /// 跟真实存在的 <c>UserId = 0</c> 分不开。
    /// </para>
    /// </summary>
    public interface ICreatedBy
    {
        long? CreatedBy { get; set; }
    }
}
