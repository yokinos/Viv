namespace Viv.Momo.Interface
{
    /// <summary>
    /// 更新时间审计能力 —— 每次更新时由 <c>MomoDatabase</c> 自动盖 <see cref="DateTime.UtcNow"/>。
    ///
    /// <para>
    /// ⚠️ <b>只写不读</b>：更新路径<b>绝不</b>碰 <see cref="ICreatedAt"/> / <see cref="ICreatedBy"/> ——
    /// 创建信息是只写一次的。反过来新增路径会同时盖四件套（创建时间与更新时间都等于新增那一刻），
    /// 否则「只插不改」的行更新时间会永远为空。
    /// </para>
    /// </summary>
    public interface IUpdatedAt
    {
        DateTime? UpdatedAt { get; set; }
    }
}
