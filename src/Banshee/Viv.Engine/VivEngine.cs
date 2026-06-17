using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Reflection;
using System.Text;
using Viv.Delusion.Extension;
using Viv.Engine.Enums;
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
        /// 从指定 JSON 文件加载
        /// </summary>
        public static VivOptions LoadVivConfig(string configFile = "viv.config.json")
        {
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
    }
}
