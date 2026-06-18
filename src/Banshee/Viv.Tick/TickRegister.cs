using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Viv.Delusion;
using Viv.Delusion.Extension;
using Viv.Delusion.Magic;
using Viv.Tick.Attributes;
using Viv.Tick.Options;
using Viv.Tick.TickerQCore;

namespace Viv.Tick
{
    public static class TickRegister
    {
        private static readonly object _lock = new();
        private static readonly List<TickerQTaskDescriptor> _pendingTasks = [];

        public static void Initialize(TickOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            VivConfigRegistry.Add(options);
        }

        /// <summary>
        /// 消费者入口：手动注册一个 ITickerQTask 及其 Cron 表达式
        /// 用于 RobinExtensions 或 Program.cs 中显式注册
        /// </summary>
        public static IServiceCollection AddVivTickerQTask<T>(this IServiceCollection services, string cron)
            where T : class, ITickerQTask
        {
            ArgumentNullException.ThrowIfNull(services);
            if (string.IsNullOrWhiteSpace(cron))
                throw new ArgumentException("Cron expression cannot be null or empty.", nameof(cron));

            AddPendingTask(new TickerQTaskDescriptor
            {
                TaskType = typeof(T),
                Cron = cron.Trim()
            });

            return services;
        }

        /// <summary>
        /// 从配置扫描：扫描 TaskTypes 指定的程序集，找到带 [VivCron] 的 ITickerQTask
        /// </summary>
        public static void ScanTasks(TickOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.TaskTypes.IsNullOrEmpty())
                return;

            var taskImplTypes = TypeScanMagic.ScanRange(options.TaskTypes);

            foreach (var type in taskImplTypes)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (!typeof(ITickerQTask).IsAssignableFrom(type))
                    continue;

                var cronAttr = type.GetCustomAttribute<VivCronAttribute>();
                if (cronAttr == null || string.IsNullOrWhiteSpace(cronAttr.Cron))
                    continue;

                AddPendingTask(new TickerQTaskDescriptor
                {
                    TaskType = type,
                    Cron = cronAttr.Cron.Trim()
                });
            }
        }

        /// <summary>
        /// 取出所有待注册任务后清空（供 ScheduleExtensions 调用）
        /// </summary>
        internal static List<TickerQTaskDescriptor> CollectPendingTasks()
        {
            lock (_lock)
            {
                var tasks = _pendingTasks
                    .DistinctBy(x => x.TaskType)
                    .ToList();

                _pendingTasks.Clear();
                return tasks;
            }
        }

        private static void AddPendingTask(TickerQTaskDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (descriptor.TaskType == null)
                throw new ArgumentException("TaskType cannot be null.", nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Cron))
                throw new ArgumentException("Cron cannot be null or empty.", nameof(descriptor));

            lock (_lock)
            {
                if (_pendingTasks.Any(x => x.TaskType == descriptor.TaskType))
                    return;

                _pendingTasks.Add(descriptor);
            }
        }
    }

    public class TickerQTaskDescriptor
    {
        public Type TaskType { get; set; } = default!;
        public string Cron { get; set; } = default!;
    }
}
