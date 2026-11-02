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
    /// 唯一拦不住的是接口代理下的自调用 —— this.OtherMethod() 走真实实例、不过接口代理，
    /// 要分析 IL 调用点才能判，本版不做，靠把特性标在最外层公开方法来规避。
    /// 类代理没有这个限制（虚派发会重新进拦截器），但代价是自调用会再开一层嵌套事务，
    /// 见 <c>CollectClassLevelAsyncMethods</c> 上方的说明。
    /// </summary>
    internal static class UnitOfWorkRegistration
    {
        /// <summary>
        /// 找出要开拦截的类型，顺便把拦不住的情况全部报出来。
        /// </summary>
        /// <param name="scannedTypes">
        /// DIOption 扫到的 Service / Repository 实现。有业务接口的走接口代理，
        /// 没有业务接口的走类代理（<c>AsSelf</c> + <c>EnableClassInterceptors</c>）。
        /// </param>
        /// <param name="dependencyTypes">
        /// 由 <c>[VivDependency]</c> / <c>IDependency</c> 自动注册的类型。用来抓「特性标在代理挂不到的类型上」。
        /// </param>
        /// <param name="databaseEnabled">是否配了 <c>VivOptions.DatabaseOption</c>。</param>
        public static Type[] Resolve(
            IEnumerable<Type> scannedTypes,
            IEnumerable<Type> dependencyTypes,
            bool databaseEnabled)
        {
            var registered = scannedTypes
                .Where(t => !t.IsGenericTypeDefinition && !t.IsAbstract)
                .Distinct()
                .ToArray();

            var registeredSet = new HashSet<Type>(registered);

            // 开放泛型注册走 RegisterGeneric，当前不挂代理（接口代理与类代理都不挂）。标了特性就是静默无事务。
            var attributedOpenGenerics = scannedTypes
                .Concat(dependencyTypes)
                .Where(t => t.IsGenericTypeDefinition && HasAttribute(t) && !IsConsumer(t))
                .Distinct()
                .ToArray();
            if (attributedOpenGenerics.Length > 0)
            {
                throw new InvalidOperationException(
                    "[VivUnitOfWork] 标在开放泛型类型上，扫描注册不会为开放泛型挂代理，事务不会生效：" +
                    string.Join(", ", attributedOpenGenerics.Select(t => t.FullName)) +
                    "。请把特性标在闭合实现上，或改用窄事务 IVivUnitOfWork。");
            }

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

            foreach (var type in registered)
            {
                if (!HasAttribute(type)) continue;

                // 没有业务接口的类型只能走 Castle 类代理：生成子类，所以类型必须 public、非 sealed、
                // 有 public 构造；被拦方法还必须 public virtual（隐式实现接口的 virtual final 不行）。
                // 这些都在下面逐条硬校验 —— 类代理失效同样是无声的（没代理上就没有事务）。
                var classProxy = ProxyPolicy.NeedsClassProxy(type);

                if (classProxy)
                {
                    ValidateClassProxyType(type);
                }

                // inherit: true —— 派生类会继承基类上的类级特性，运行时的 ShouldIntercept 同样沿基类链找，
                // 注册期只看自身（inherit: false）的话，派生类进了 intercepted 却不做类级校验，
                // 里面新写的非虚方法就静默没有事务。
                var classLevel = type.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true);

                // ② 方法级特性：逐条严查（这是 API 侧的主路径，必须能拦到）
                //    必须带上 Static —— 漏了它静态方法根本不在枚举结果里，
                //    "标了特性的静态方法"会被静默放行（ValidateMethod 里的 IsStatic 判断也就成了死代码）
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Instance | BindingFlags.Static
                                                       | BindingFlags.DeclaredOnly))
                {
                    var methodAttribute = method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: false);
                    if (methodAttribute == null) continue;
                    if (!methodAttribute.Enabled) continue;

                    ValidateMethod(type, method);
                    if (classProxy) RequireVirtual(type, method);
                    methodCount++;
                }

                // ③ 类级特性：与方法级同一把尺子。标了就必须能拦到，否则启动失败。
                //    Enabled = false 是明确的「这个类不要事务」—— 派生类去掉基类继承来的特性只有这一个办法
                //    （AttributeUsage 是 Inherited = true），所以它必须真的豁免，不能标了反而启动失败。
                if (classLevel is { Enabled: true })
                {
                    methodCount += CollectClassLevelAsyncMethods(type, requireVirtual: classProxy);
                }

                intercepted.Add(type);
            }

            // ④ 特性标在「扫描注册够不到」的类型上 —— 最隐蔽的一种失效：能编译、能跑、就是没事务。
            //    无接口类代理只支持 DIOption 扫描注册（Scoped），[VivDependency(AsSelf = true)] 那条路
            //    没有挂拦截器的地方，生命周期也不一定是 Scoped（拦截器是 Scoped，单例会 captive）。
            foreach (var type in dependencyTypes.Distinct())
            {
                if (registeredSet.Contains(type)) continue;
                if (!HasAttribute(type)) continue;
                if (IsConsumer(type)) continue;                 // 消费者走类级显式读取，不经代理

                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在 {type.FullName} 上，但该类型不是由 DIOption 扫描注册的" +
                    "（可能标了 [VivDependency(AsSelf = true)]，或不在 DIOption 扫描范围内）—— " +
                    "代理挂不上去，事务不会生效。请把它移进 ServiceImplementation / RepositoryImplementation " +
                    "的扫描范围，或为它定义接口后按接口注册。");
            }

            UnitOfWorkDiagnostics.RecordScan(intercepted.Count, methodCount);
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

            // 接口代理不要求 virtual：CreateInterfaceProxyWithTarget 按接口调用、反射转给目标实例，
            // 非 virtual（含隐式实现接口的 virtual + final）同样能拦到 —— 有测试钉着。
            // 类代理（无业务接口）必须 public virtual，由 RequireVirtual 单独校验。

            if (method.IsGenericMethodDefinition)
                throw new InvalidOperationException($"[VivUnitOfWork] 标在泛型方法上，代理拦不到：{where}");

            // 实测：同步方法与全部 ValueTask / ValueTask<T> 都不能安全走异步拦截链。
            // 非泛型 ValueTask 走 InterceptSynchronous，事务根本不开；
            // ValueTask<T> 会进 InterceptAsync<T> 但提交发生在方法体结束之前（事务边界错位且不报错）。
            // 一律启动失败，逼业务改成 Task / Task<T>。
            if (method.ReturnType == typeof(ValueTask)
                || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] 标在返回 ValueTask / ValueTask<T> 的方法上：{where}。" +
                    "Castle AsyncInterceptor 不能安全拦截 ValueTask：提交会早于方法体结束（或根本不开事务）。" +
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
        /// 无业务接口的类型只能走 Castle 类代理 —— 类代理是生成子类，所以类型必须
        /// public、非 sealed，且有 public 构造函数（Autofac 只按 public ctor 选）。
        /// 任何一条不满足，代理都根本生成不出来；不在这里挡住就会在首次解析时才炸。
        /// </summary>
        private static void ValidateClassProxyType(Type type)
        {
            if (!type.IsVisible)
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] {type.FullName} 没有业务接口，只能走类代理，但该类型不是 public —— " +
                    "Castle 无法在动态程序集里继承它。请把类型改成 public（internal 还要给业务程序集加 " +
                    "InternalsVisibleTo(\"DynamicProxyGenAssembly2\")，本仓没有这条约定），或为它定义接口。");
            }

            if (type.IsSealed)
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] {type.FullName} 没有业务接口，只能走类代理，但该类型是 sealed —— " +
                    "Castle 生成不出子类。请去掉 sealed，或为它定义接口。");
            }

            if (type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0)
            {
                throw new InvalidOperationException(
                    $"[VivUnitOfWork] {type.FullName} 没有业务接口，只能走类代理，但没有 public 构造函数 —— " +
                    "Autofac 选不到构造，代理生成不出来。请补一个 public 构造函数，或为它定义接口。");
            }
        }

        /// <summary>
        /// 类代理只能拦 public virtual 方法。非 virtual、sealed override（<c>IsFinal</c>）
        /// 在类代理下都是静默无事务，必须启动失败。
        /// </summary>
        private static void RequireVirtual(Type type, MethodInfo method)
        {
            if (method.IsVirtual && !method.IsFinal) return;

            throw new InvalidOperationException(
                $"[VivUnitOfWork] 无业务接口的类型走类代理，只能拦 public virtual 方法：{type.FullName}.{method.Name}。" +
                "非 virtual 或 sealed override 都拦不到，请加 virtual，或为它定义接口。");
        }

        /// <summary>
        /// 类级特性下，每个公开实例方法都必须能被拦截（与方法级同一套 ValidateMethod），
        /// 否则「标了特性 = 有事务」不成立。方法上显式 <c>Enabled = false</c> 的跳过。
        ///
        /// <para>类代理下额外要求 public virtual。注意：类代理会拦 <c>this.VirtualMethod()</c>
        /// （虚派发），自调用会再开一层嵌套事务 —— 现有嵌套语义是子句柄、提交 no-op，安全。</para>
        /// </summary>
        private static int CollectClassLevelAsyncMethods(Type type, bool requireVirtual)
        {
            var count = 0;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue;              // 属性存取器不单独计数

                var methodAttribute = method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: false);
                if (methodAttribute is { Enabled: false }) continue;

                ValidateMethod(type, method);
                if (requireVirtual) RequireVirtual(type, method);
                count++;
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
        /// 消费者（<c>VivConsumer&lt;T&gt;</c> / <c>VivLocalConsumer&lt;T&gt;</c>）豁免接口代理校验 ——
        /// Worker 侧事务由消费者基类的 HandleAsync 按类级 / ReceiveMessageAsync 上的特性显式开启。
        /// 没配 DatabaseOption 却标了特性，仍走上面「anyoneAttributed」那条硬失败。
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
        /// 能被异步拦截链接管的返回类型。ValueTask / ValueTask&lt;T&gt; 一律不在此列。
        /// </summary>
        private static bool IsAsyncReturn(Type returnType)
        {
            if (returnType == typeof(Task)) return true;
            if (!returnType.IsGenericType) return false;

            return returnType.GetGenericTypeDefinition() == typeof(Task<>);
        }
    }
}
