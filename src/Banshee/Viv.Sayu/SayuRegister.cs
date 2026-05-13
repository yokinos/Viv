using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Viv.Sayu.Options;
using Viv.Vva;
using Viv.Vva.Extension;
using Viv.Vva.Magic;

namespace Viv.Sayu
{
    public static class SayuRegister
    {
        private static readonly List<TickerQTaskDescriptor> _pendingTasks = [];

        public static void Initialize(SayuOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            VivConfigRegistry.Add(options);
        }

        /// <summary>
        /// 消费者入口：手动注册一个 ITickerQTask 及其 Cron 表达式
        /// 用于 RobinExtensions 或 Program.cs 中显式注册
        /// </summary>
        public static IServiceCollection AddVivTickerQTask<T>(this IServiceCollection services, string cron)
            where T : class
        {
            _pendingTasks.Add(new TickerQTaskDescriptor
            {
                TaskType = typeof(T),
                Cron = cron
            });
            return services;
        }

        /// <summary>
        /// 从配置扫描：扫描 TaskTypes 指定的程序集，找到带 [VivCron] 的 ITickerQTask
        /// </summary>
        public static void ScanTasks(SayuOptions options)
        {
            if (options.TaskTypes.IsNullOrEmpty()) return;

            var taskImplTypes = TypeScanMagic.ScanRange(options.TaskTypes);
            foreach (var type in taskImplTypes)
            {
                var cronAttr = type.GetCustomAttribute<VivCronAttribute>();
                if (cronAttr == null) continue;

                _pendingTasks.Add(new TickerQTaskDescriptor
                {
                    TaskType = type,
                    Cron = cronAttr.Cron
                });
            }
        }

        /// <summary>
        /// 取出所有待注册任务后清空（供 ScheduleExtensions 调用）
        /// </summary>
        internal static List<TickerQTaskDescriptor> CollectPendingTasks()
        {
            var tasks = new List<TickerQTaskDescriptor>(_pendingTasks);
            _pendingTasks.Clear();
            return tasks;
        }
    }

    public class TickerQTaskDescriptor
    {
        public Type TaskType { get; set; } = default!;
        public string Cron { get; set; } = default!;
    }
}
