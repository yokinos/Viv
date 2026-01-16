using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Viv.Vva
{
    /// <summary>
    /// 用来读取嵌入式配置文件的类
    /// </summary>
    public class EmbeddedConfigExtractor
    {
        /// <summary>
        /// 从指定程序集提取嵌入式配置文件到指定路径
        /// </summary>
        /// <param name="assemblyName">程序集名称</param>
        /// <param name="resourceName">嵌入式资源名称</param>
        /// <param name="outputPath">输出到的文件路径</param>
        public static void ExtractConfig(string assemblyName, string resourceName, string outputPath)
        {
            var fullResourceName = $"{assemblyName}.{resourceName}";
            var assembly = Assembly.Load(assemblyName);
            if (assembly == null) return;

            using var resourceStream = assembly.GetManifestResourceStream(fullResourceName);
            if (resourceStream == null) return;

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 读取配置文件内容
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            resourceStream.CopyTo(fileStream);
        }

        /// <summary>
        /// 检查配置文件是否存在，不存在则提取内置配置
        /// </summary>
        /// <param name="configPath">配置文件路径</param>
        /// <param name="extractAction">提取配置的委托</param>
        public static void EnsureConfigExists(string configPath, Action<string> extractAction)
        {
            if (!File.Exists(configPath))
            {
                extractAction(configPath);
            }
        }
    }
}