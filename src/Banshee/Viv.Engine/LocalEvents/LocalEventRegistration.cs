using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using Viv.Contracts.Events;
using Viv.Contracts.Interface;
using Viv.Delusion.Magic;

namespace Viv.Engine.LocalEvents
{
    /// <summary>
    /// 本地事件的扫描注册，由 <see cref="VivRegister.Register"/> 调用。
    ///
    /// 处理器无需打特性、无需实现 IDependency，实现 IVivLocalEventHandler&lt;TEvent&gt;
    /// 或继承 LocalEventHandler&lt;TEvent&gt; 即被扫到（基类实现了接口，两者同一条代码路径）。
    ///
    /// 扫到 0 个处理器是合法的，只记日志不报错 —— 与业务 Service 注册（扫到 0 个通常意味着配置写错）不同。
    /// </summary>
    internal static class LocalEventRegistration
    {
        /// <summary>扫描到的处理器类型数（进程内诊断用，总线首次构造时打印）</summary>
        public static int HandlerCount { get; private set; }

        /// <summary>扫描到的事件类型数（进程内诊断用，总线首次构造时打印）</summary>
        public static int EventTypeCount { get; private set; }

        public static void Register(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // 业务 Core 程序集是懒加载的，不先强制加载传递引用，扫描会静默漏掉全部处理器
            // （同 VivRegister.RegisterNana 处理 Saga 类型时的做法）
            TypeScanMagic.ForceLoadReferencedAssemblies();

            // ScanTypes 已支持开放泛型匹配：TypeScanMagic.IsMatchType 对开放泛型
            // 走「基类链/接口的 GetGenericTypeDefinition」比对，所以直接传开放泛型接口即可
            var handlerTypes = TypeScanMagic.ScanTypes(typeof(IVivLocalEventHandler<>));
            var eventTypes = new HashSet<Type>();

            foreach (var handlerType in handlerTypes)
            {
                // 泛型处理器定义（如 MyHandler&lt;T&gt; : IVivLocalEventHandler&lt;E&gt;）无法自动注册 ——
                // TypeScanMagic.IsMatchType 对开放泛型走 GetGenericTypeDefinition 比对，会把这类定义也扫进来，
                // 而闭合 TEvent 未知，继续走下面的 AddScoped / MakeGenericType 会直接抛异常。跳过。
                if (handlerType.IsGenericTypeDefinition)
                    continue;

                // 必须遍历全部闭合接口：一个处理器订阅多个事件时，
                // 只取第一个会漏注册其余的（配套回归测试守着这一点）
                foreach (var iface in handlerType.GetInterfaces().Where(IsLocalEventHandlerInterface))
                {
                    var eventType = iface.GetGenericArguments()[0];
                    eventTypes.Add(eventType);
                    services.AddScoped(iface, handlerType);
                }
            }

            // 每个事件类型注册一个闭合分发器：把「运行时类型 → 处理器集合」的解析
            // 从 flush 时的反射挪到启动期，运行期只做一次字典查表
            foreach (var eventType in eventTypes)
            {
                services.AddScoped(typeof(ILocalEventHandlerInvoker),
                    typeof(LocalEventHandlerInvoker<>).MakeGenericType(eventType));
            }

            services.AddScoped<IVivLocalEventBus, LocalEventBus>();

            HandlerCount = handlerTypes.Count;
            EventTypeCount = eventTypes.Count;
        }

        /// <summary>判断接口是否为闭合的 IVivLocalEventHandler&lt;TEvent&gt;</summary>
        private static bool IsLocalEventHandlerInterface(Type iface)
            => iface.IsGenericType
               && iface.GetGenericTypeDefinition() == typeof(IVivLocalEventHandler<>);
    }
}
