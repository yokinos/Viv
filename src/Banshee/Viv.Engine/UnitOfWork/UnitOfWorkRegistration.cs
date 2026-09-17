using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Viv.Contracts.Attributes;
using Viv.Nana;

namespace Viv.Engine.UnitOfWork
{
    /// <summary>
    /// 工作单元的注册期筛选与启动期校验。
    ///
    /// 校验做得重是因为代理失效完全无声：没代理上就没有事务，业务照常执行、数据照常写入，
    /// 只是不再原子，不报错也不记日志，往往到线上数据对不上才发现。凡是能静态判定的失效原因都前移到启动期硬报错。
    ///
    /// 唯一拦不住的是自调用 —— this.OtherMethod() 走真实实例、不过代理，要分析 IL 调用点才能判，
    /// 本版不做，靠把特性标在最外层公开方法来规避。
    /// </summary>
    internal static class UnitOfWorkRegistration
    {
        /// <summary>
        /// 找出要开拦截的类型，顺便把拦不住的情况全部报出来。
        /// </summary>
        /// <param name="interfaceRegisteredTypes">
        /// 会被 <c>AsImplementedInterfaces()</c> 注册的类型（DIOption 扫到的 Service / Repository 实现）。
        /// 只有这些类型能生成接口代理。
        /// </param>
        /// <param name="dependencyTypes">
        /// 由 <c>[VivDependency]</c> / <c>IDependency</c> 自动注册的类型。用来抓「特性标在没接口的类型上」。
        /// </param>
        /// <param name="databaseEnabled">是否配了 <c>VivOptions.DatabaseOption</c>。</param>
        public static Type[] Resolve(
            IEnumerable<Type> interfaceRegisteredTypes,
            IEnumerable<Type> dependencyTypes,
            bool databaseEnabled)
        {
            var registered = interfaceRegisteredTypes
                .Where(t => !t.IsGenericTypeDefinition && !t.IsAbstract)
                .Distinct()
                .ToArray();

            var registeredSet = new HashSet<Type>(registered);

            // ① 数据库没开却标了特性 —— 拦截器解析不到 IVivUnitOfWork，运行期必炸，提前说清楚
            var anyoneAttributed = registered.Concat(dependencyTypes).Any(HasAttribute);
            if (anyoneAttributed && !databaseEnabled)
            {
                throw new InvalidOperationException(
                    "检测到 [VivUnitOfWork]，但 VivOptions.DatabaseOption 未配置 —— " +
                    "工作单元依赖数据库事务，请先配好数据库，或移除该特性。");
            }

            var intercepted = new List<Type>();
            var methodCount = 0;
            var notCovered = new List<string>();

            foreach (var type in registered)
            {
                if (!HasAttribute(type)) continue;

                if (type.GetInterfaces().Length == 0)
                {
                    throw new InvalidOperationException(
                        $"[VivUnitOfWork] 标在 {type.FullName} 上，但该类型没有任何接口 —— " +
                        "接口代理生成不出来，事务不会生效。请为它定义接口，或改用窄事务 IVivUnitOfWork。");
                }

                var classLevel = type.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: false);

                // ② 方法级特性：逐条严查（这是 API 侧的主路径，必须能拦到）
                //    必须带上 Static —— 漏了它静态方法根本不在枚举结果里，
                //    "标了特性的静态方法"会被静默放行（ValidateMethod 里的 IsStatic 判断也就成了死代码）
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Instance | BindingFlags.Static
                                                       | BindingFlags.DeclaredOnly))
                {
                    var methodAttribute = method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: false);
                    if (methodAttribute == null) continue;

                    ValidateMethod(type, method);

                    if (methodAttribute.Enabled) methodCount++;
                }

                // ③ 类级特性：只对可重写的异步方法生效，其余的收集起来启动时告警
                if (classLevel != null)
                {
                    methodCount += CollectClassLevelAsyncMethods(type, notCovered);
                }

                intercepted.Add(type);
            }

            // ④ 特性标在「不按接口注册」的类型上 —— 最隐蔽的一种失效：能编译、能跑、就是没事务
            foreach (var type in dependencyTypes.Distinct())
            {
                if (registeredSet.Contains(type)) continue;
                if (!HasAttribute(type)) continue;
                if (IsConsumer(type)) continue;                 // 消费者走类级显式读取，不经代理

                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在 {type.FullName} 上，但该类型不是按接口注册的" +
                    "（可能标了 [VivDependency(AsSelf = true)]，或不在 DIOption 扫描范围内）—— " +
                    "没有接口就生成不出代理，事务不会生效。请改为按接口注册。");
            }

            UnitOfWorkDiagnostics.RecordScan(intercepted.Count, methodCount, notCovered);
            return intercepted.ToArray();
        }

        /// <summary>
        /// 方法级特性必须满足的四条。任何一条不满足都是「静默无事务」，一律硬报错。
        /// </summary>
        private static void ValidateMethod(Type type, MethodInfo method)
        {
            var where = $"{type.FullName}.{method.Name}";

            if (!method.IsPublic)
                throw new InvalidOperationException($"[VivUnitOfWork] 标在非 public 方法上，代理拦不到：{where}");

            if (method.IsStatic)
                throw new InvalidOperationException($"[VivUnitOfWork] 标在静态方法上，代理拦不到：{where}");

            // 只判 !IsVirtual 是错的（实测踩过）：C# 会把隐式实现接口的 public 方法编译成
            // virtual + final，IsVirtual 为 true 但 sealed 重写不了 —— 那条检查恰好把最容易
            // 写出的方法放了过去。决定「Castle 能不能重写」的是 IsVirtual && !IsFinal。
            if (!method.IsVirtual || method.IsFinal)
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在不可重写的方法上（非 virtual，或已 sealed）：{where}。" +
                    "接口代理拦不到它，事务不会生效。请显式加 virtual。");

            if (method.IsGenericMethodDefinition)
                throw new InvalidOperationException($"[VivUnitOfWork] 标在泛型方法上，代理拦不到：{where}");

            // 实测：同步方法与非泛型 ValueTask 都走 AsyncInterceptorBase 不可重写的同步路径
            // （InterceptSynchronous），表现是「方法照常执行、事务根本没开」，完全静默。
            // 泛型版 ValueTask<T> 正常走异步链。这条差异反直觉，所以照实测结果拦。
            if (method.ReturnType == typeof(ValueTask))
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在返回非泛型 ValueTask 的方法上：{where}。" +
                    "非泛型 ValueTask 不经过异步拦截链（ValueTask<T> 才走），事务不会生效。" +
                    "请改为返回 Task / Task<T>，或改用窄事务 IVivUnitOfWork。");
            }

            if (!IsAsyncReturn(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在同步方法上：{where} 返回 {method.ReturnType.Name}。" +
                    "同步方法不会开启事务（异步拦截链不处理它），请改为返回 Task / Task<T>，或改用窄事务 IVivUnitOfWork。");
            }
        }

        /// <summary>
        /// 类级特性下，数一遍会被覆盖的异步方法，同时把没覆盖到的公开方法逐个收集起来告警。
        /// 类级特性承诺「整个类都是事务的」，但接口代理只拦得住可重写的异步方法，剩下的一律要报出来。
        /// </summary>
        private static int CollectClassLevelAsyncMethods(Type type, List<string> notCovered)
        {
            var count = 0;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue;              // 属性存取器不单独计数

                // 隐式实现接口的 public 方法在元数据里是 virtual + final（见 ValidateMethod 里的实测说明），
                // 光看 IsVirtual 会把它当成「已覆盖」
                if (!method.IsVirtual || method.IsFinal)
                {
                    notCovered.Add($"{type.FullName}.{method.Name}（不可重写：非 virtual 或已 sealed）");
                    continue;
                }

                if (IsAsyncReturn(method.ReturnType))
                {
                    count++;
                }
                else
                {
                    notCovered.Add($"{type.FullName}.{method.Name}（返回 {method.ReturnType.Name}，同步方法不开事务）");
                }
            }

            return count;
        }

        private static bool HasAttribute(Type type)
        {
            if (type.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true) != null) return true;

            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                       .Any(m => m.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true) != null);
        }

        /// <summary>
        /// 消费者（<c>VivConsumer&lt;T&gt;</c> / <c>VivLocalConsumer&lt;T&gt;</c>）豁免 ——
        /// Worker 侧事务由消费者基类的 HandleAsync 显式读类级特性来开，不走接口代理。
        /// </summary>
        private static bool IsConsumer(Type type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
            {
                if (!t.IsGenericType) continue;

                var definition = t.GetGenericTypeDefinition();
                if (definition == typeof(VivConsumer<>) || definition == typeof(VivLocalConsumer<>))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 能被异步拦截链接管的返回类型。非泛型 <c>ValueTask</c> 不在此列，实测它走同步路径。
        /// </summary>
        private static bool IsAsyncReturn(Type returnType)
        {
            if (returnType == typeof(Task)) return true;
            if (!returnType.IsGenericType) return false;

            var definition = returnType.GetGenericTypeDefinition();
            return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
        }
    }
}
