namespace Viv.Momo.Interface
{
    /// <summary>
    /// 创建人审计能力，新增时由 <c>MomoDatabase</c> 盖 <c>IVivContext.UserId</c>。
    /// 无登录上下文（Worker / 消息消费 / 后台任务）记 <c>null</c>，不记 0。
    /// </summary>
    public interface ICreatedBy
    {
        long? CreatedBy { get; set; }
    }
}
