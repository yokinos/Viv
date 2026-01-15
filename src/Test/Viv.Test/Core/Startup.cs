using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Viv.Test.Common;

namespace Viv.Test.Core
{
    public class Startup
    {
        private static readonly Dictionary<string, Assembly> _assemblyCache = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public static async Task RunAsync()
        {
            Console.Title = "Viv 测试程序";
            Console.WriteLine(ConsoleContext.FirstMessage);
            var commandList = ConsoleContext.GetCmdAssemblys();

            try
            {
                var input = string.Empty;
                var exitCommand = Command.Exit.ToString();
                while (!input.Equals(exitCommand, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        await ProcessCommandAsync(input.Trim(), commandList);
                    }

                    input = Console.ReadLine()?.Trim() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Out.PrintlnError(ex);
                Out.PrintlnWarning("按任意键退出...");
                Console.ReadLine();
            }
        }

        private static async Task ProcessCommandAsync(string inputText, List<CommandModel> commandList)
        {
            var commandModel = commandList.FirstOrDefault(x => x.Command.Equals(inputText, StringComparison.OrdinalIgnoreCase));
            if (commandModel == null)
            {
                Out.PrintlnError($"未知的命令：[{inputText}]");
                return;
            }

            if (string.IsNullOrWhiteSpace(commandModel.AssemblyName) || string.IsNullOrWhiteSpace(commandModel.ClassFullName))
            {
                Out.PrintlnError($"命令[{inputText}]配置异常：程序集名或类全名不能为空");
                return;
            }

            var assembly = await LoadAssemblyAsync(commandModel.AssemblyName);
            if (assembly == null)
            {
                Out.PrintlnError($"未找到程序集:{commandModel.AssemblyName}");
                return;
            }

            var targetType = assembly.GetType(commandModel.ClassFullName);
            if (targetType == null)
            {
                Out.PrintlnError($"无法加载类型:{commandModel.ClassFullName}，请确保类型全名正确");
                return;
            }

            var testTaskInterface = typeof(ITestTask);
            if (!testTaskInterface.IsAssignableFrom(targetType))
            {
                Out.PrintlnError($"类型[{commandModel.ClassFullName}]未实现接口[{testTaskInterface.Name}]，无法执行");
                return;
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(targetType);
            }
            catch (MissingMethodException)
            {
                Out.PrintlnError($"类型[{commandModel.ClassFullName}]缺少无参构造函数，无法创建实例");
                return;
            }
            catch (Exception ex)
            {
                Out.PrintlnError($"类型[{commandModel.ClassFullName}]创建实例失败：{ex.Message}");
                return;
            }

            if (instance == null)
            {
                Out.PrintlnError($"类型:{commandModel.ClassFullName}创建实例失败!");
                return;
            }

            Console.Write("-->执行任务：[ ");
            Out.Write(targetType.FullName, ConsoleColor.Blue);
            Console.Write(" ]");
            Console.WriteLine();
            try
            {
                var testTask = (ITestTask)instance;
                await testTask.StartAsync();
                if (commandModel.CommandType == CommandType.Custom)
                {
                    Out.PrintlnSuccess($"[ The task is completed ]");
                }
            }
            catch (Exception ex)
            {
                Out.Println(ex, ConsoleColor.Red);
            }
        }

        private static Task<Assembly> LoadAssemblyAsync(string assemblyName)
        {
            if (_assemblyCache.TryGetValue(assemblyName, out var cachedAssembly))
            {
                return Task.FromResult(cachedAssembly);
            }

            var loadedAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.ManifestModule.Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase));

            if (loadedAssembly != null)
            {
                _assemblyCache[assemblyName] = loadedAssembly;
                return Task.FromResult(loadedAssembly);
            }

            string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assemblyName);
            if (!File.Exists(assemblyPath))
            {
                return Task.FromResult<Assembly>(null);
            }

            try
            {
                var newAssembly = Assembly.LoadFrom(assemblyPath);
                _assemblyCache[assemblyName] = newAssembly;
                return Task.FromResult(newAssembly);
            }
            catch (Exception ex)
            {
                Out.PrintlnError($"加载程序集[{assemblyName}]失败：{ex.Message}");
                return Task.FromResult<Assembly>(null);
            }
        }
    }
}