using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Engine.Options;
using Viv.Sandrone.Conveter;
using Viv.Sandrone.Impl;

#nullable disable
namespace Viv.Engine
{
    /// <summary>
    /// Viv框架全局引擎入口
    /// 持有全局配置快照与请求上下文静态快捷访问
    /// </summary>
    public sealed class VivEngine
    {
        private static volatile VivOptions _vivOptions;
        private static DateTime? _vivAppStartTime;

        private VivEngine() { }

        /// <summary>
        /// Viv框架全局配置
        /// </summary>
        public static VivOptions VivOptions { get => _vivOptions; }

        /// <summary>
        /// Viv应用启动时间
        /// </summary>
        public static DateTime? VivAppStartTime => _vivAppStartTime;

        /// <summary>
        /// 上下文访问器（容器实时解析，不缓存实例）
        /// </summary>
        private static IVivContextAccessor Accessor => VivLocator.GetAutofaService<IVivContextAccessor>();

        /// <summary>
        /// 获取当前线程请求上下文快照
        /// 【语法糖，控制器/过滤器临时使用】
        /// 领域Service、仓储优先注入 IVivContext，禁止大量使用该静态入口
        /// </summary>
        public static VivContextContent CurrentSnapshot => Accessor.Current;

        /// <summary>
        /// 写入全局配置快照。绑定本身（读 IConfiguration、VivOptions__* 环境变量覆盖）在
        /// <see cref="VivConfigLoader.Load"/>，那里是全进程唯一的绑定入口，这里只负责落快照。
        ///
        /// 快照就是绑定产物本身，不再是深拷贝副本 —— 谁再去拷一份，静态读取点与 DI 那份就会是两个对象图。
        /// 内部可见是因为调用方 VivConfigLoader 住在同一个程序集，业务侧不应直接写它。
        /// </summary>
        internal static void SetVivOptions(VivOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _vivOptions = options;
            _vivAppStartTime = DateTime.Now;
        }
    }
}
