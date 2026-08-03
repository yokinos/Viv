using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;
using Viv.Aoi;
using Viv.Contracts.Interface;
using Viv.Contracts.Models;
using Viv.Delusion.Extension;
using Viv.Engine.Options;
using Viv.Sandrone.Impl;

#nullable disable
namespace Viv.Engine
{
    /// <summary>
    /// Viv框架全局引擎入口
    /// 提供全局配置加载、请求上下文静态快捷访问
    /// </summary>
    public sealed class VivEngine
    {
        private static volatile VivOptions _vivOptions;
        private static DateTime? _vivAppStartTime;

        private VivEngine() { }

        public static VivOptions VivOptions { get => _vivOptions; }
        
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
        public static VivContextModel CurrentSnapshot => Accessor.Current;

        /// <summary>
        /// 从指定 JSON 文件加载
        /// </summary>
        public static VivOptions LoadVivConfig(string configFile = "viv.config.json")
        {
            _vivAppStartTime = DateTime.Now;
            var options = LoadFromJsonFile(configFile);
            _vivOptions = options.DeepCopy();
            return options;
        }

        private static VivOptions LoadFromJsonFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new VivOptions();

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<VivOptions>(json) ?? new VivOptions();
        }
    }
}
