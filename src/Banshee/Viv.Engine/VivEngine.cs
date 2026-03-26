using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Viv.Engine.Options;

#nullable disable
namespace Viv.Engine
{
    public sealed class VivEngine
    {
        private static volatile VivOptions _vivOptions;

        /// <summary>
        /// Viv配置选项
        /// </summary>
        public static VivOptions VivOptions { get => _vivOptions; }

        /// <summary>
        /// 不允许实例化
        /// </summary>
        private VivEngine() { }

        /// <summary>
        /// 加载Viv配置选项
        /// </summary>
        /// <param name="configfile"></param>
        /// <returns></returns>
        public static VivOptions LoadVivConfig(string configfile = "viv.config.json")
        {
            var json = File.ReadAllText(configfile, Encoding.UTF8);
            var options = JsonConvert.DeserializeObject<VivOptions>(json)!;
            if (options != null)
            {
                _vivOptions = options;
            }

            return options;
        }
    }
}
