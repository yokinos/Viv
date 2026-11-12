namespace Viv.Contracts.Models
{
    /// <summary>
    /// Viv 数据域主体常量定义
    /// <para>说明：<see cref="VivClaimTypes.SubjectId"/> 作为数据隔离标识，0 代表无主体场景（匿名请求、后台任务、未登录），不可用作平台主体；</para>
    /// <para>平台运营侧需使用独立非0主体Id，避免 0 混入租户过滤、权限白名单逻辑引发语义混淆。</para>
    /// </summary>
    public static class VivSubject
    {
        /// <summary>
        /// 平台主体ID
        /// </summary>
        public const long PlatformId = 12345678910L;
    }
}
