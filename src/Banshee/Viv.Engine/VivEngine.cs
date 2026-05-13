using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;
using Viv.Engine.Options;

#nullable disable
namespace Viv.Engine
{
    public sealed class VivEngine
    {
        private static volatile VivOptions _vivOptions;

        public static VivOptions VivOptions { get => _vivOptions; }

        private VivEngine() { }

        /// <summary>
        /// 从 IConfiguration 加载 Viv 配置（主入口）。
        /// 1. 先读 viv.{service}.json 作为基底
        /// 2. 再用 IConfiguration["Viv"] 覆盖（Aspire 环境变量在这里生效）
        /// 好处：无 Aspire 时文件层兜底；有 Aspire 时环境变量覆盖文件值
        /// </summary>
        public static VivOptions LoadVivConfig(IConfiguration configuration)
        {
            // 1. 基底 — 从文件加载
            var options = LoadFromFile();

            // 2. 覆盖 — IConfiguration（含 Aspire 环境变量）覆盖匹配的属性
            configuration.GetSection("Viv").Bind(options);

            _vivOptions = options;
            return options;
        }

        /// <summary>
        /// 仅从 viv.{service}.json 文件加载（无 Aspire / 非 Web 场景）
        /// </summary>
        public static VivOptions LoadVivConfig()
        {
            return LoadFromFile();
        }

        /// <summary>
        /// 从指定 JSON 文件加载（覆盖默认文件名）
        /// </summary>
        public static VivOptions LoadVivConfig(string configFile)
        {
            var options = LoadFromJsonFile(configFile);
            _vivOptions = options;
            return options;
        }

        private static VivOptions LoadFromFile()
        {
            var serviceName = ResolveServiceName();
            var configFile = $"viv.{serviceName}.json";
            var options = LoadFromJsonFile(configFile);
            _vivOptions = options;
            return options;
        }

        private static VivOptions LoadFromJsonFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new VivOptions();

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<VivOptions>(json) ?? new VivOptions();
        }

        private static string ResolveServiceName()
        {
            var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;

            if (string.IsNullOrEmpty(assemblyName))
                return "default";

            var segments = assemblyName.Split('.');
            var serviceSegments = segments
                .SkipWhile(s => s.Equals("Viv", StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .SkipWhile(s => s is "Api" or "Core" or "Link")
                .Reverse()
                .ToArray();

            return serviceSegments.Length > 0
                ? string.Join("-", serviceSegments).ToLowerInvariant()
                : "default";
        }
    }
}
