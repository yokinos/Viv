using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;
using Viv.Engine.Enums;
using Viv.Engine.Options;
using Viv.Vva.Extension;

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
        public static VivOptions LoadVivConfig(string configFile= "viv.config.json")
        {
            var options = LoadFromJsonFile(configFile);
            _vivOptions = options;
            return options;
        }

        private static VivOptions LoadFromFile()
        {
            var configFile = $"viv.config.json";
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


        /// <summary>
        /// 创建默认的Viv配置
        /// </summary>
        /// <param name="configuration"></param>
        /// <returns></returns>
        public static VivOptions CreateVivDefaultOptionsFromAsipre(IConfiguration configuration,string db)
        {
            var aspireOptions = configuration.GetSection("AspireParameter").Value.As<AspireParameter>();
            ArgumentNullException.ThrowIfNull(aspireOptions);

            var options = LoadFromJsonFile("viv.config.json");
            ArgumentNullException.ThrowIfNull(options);


            return options;
        }
    }
}
