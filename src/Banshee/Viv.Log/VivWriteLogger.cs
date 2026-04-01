using System;
using System.Threading.Tasks;
using Viv.Aoi;

namespace Viv.Log
{
    /// <summary>
    /// 静态日志工具类（内部使用，提供极简的静态方法日志记录）
    /// </summary>
    /// <remarks>
    /// 1. 懒加载初始化日志器，首次调用时自动匹配LogOptions配置；
    /// 2. 无需依赖注入，直接通过静态方法调用（Info/Error等）；
    /// 3. 未配置日志框架时自动使用NoneLogger（控制台输出）兜底；
    /// 4. 同步/异步方法双支持，异步方法基于ValueTask实现零堆分配优化；
    /// 5. 在WebAPI/ASP.NET Core等依赖注入场景中，推荐使用构造函数注入IVivLogger实例，以支持：
    ///    - 更灵活的日志器生命周期管理；
    ///    - 不同模块使用不同日志配置；
    ///    - 单元测试时轻松模拟日志器。
    /// </remarks>
    public static class VivWriteLogger
    {
        // 懒加载获取日志器实例
        private static IDistributedLogger Logger => VivLocator.GetAutofaService<IDistributedLogger>();

        /// <summary>
        /// 记录Error级别日志（仅消息）
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void Error(string message)
        {
            Logger.Error(message);
        }
    }
}