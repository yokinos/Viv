using Viv.Delusion.Magic;

namespace Viv.Aoi
{
    /// <summary>
    /// 自动依赖注入配置
    /// </summary>
    public class DIOptions
    {
        /// <summary>
        /// 服务实现扫描规则
        /// </summary>
        public FilterTypeOptions ServiceImplementation { get; set; }

        /// <summary>
        /// 仓储实现扫描规则
        /// </summary>
        public FilterTypeOptions RepositoryImplementation { get; set; }
    }
}