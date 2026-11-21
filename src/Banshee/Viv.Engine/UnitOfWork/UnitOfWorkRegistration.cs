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

                // ② 方法级特性：枚举「会真正被代理命中的方法集」—— 继承链上的 public 实例方法
                //    （override 只留最派生的那一个，正是运行期 MethodInvocationTarget 指向的方法），
                //    特性沿 override 链找，与运行期 ShouldIntercept 的 inherit: true 对齐。
                //    只用 DeclaredOnly + inherit: false 的话：「基类标了、子类重写不标」整条跳过校验，
                //    「基类标了、子类不重写」连 HasAttribute 都发现不了 —— 类型不进拦截清单，静默无事务。
                //
                //    这里不跳 IsSpecialName（类级那趟要跳）：显式标在属性访问器上的事务一定开不了 ——
                //    存取器是同步方法，异步拦截链管不了它 —— 必须报错，不能像类级那样放行。
                foreach (var method in GetInterceptableMethods(type))
                {
                    var methodAttribute = method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true);
                    if (methodAttribute is not { Enabled: true }) continue;

                    ValidateMethod(type, method);
                    if (classProxy) RequireVirtual(type, method);
                    else RequireInterfaceMethod(type, method);
                    methodCount++;
                }

                // ③ 标在非 public / static 方法上：代理一定拦不到，也不在上面那趟有效方法集里。
                //    必须带上 Static —— 漏了它静态方法根本不在枚举结果里，
                //    "标了特性的静态方法"会被静默放行（ValidateMethod 里的 IsStatic 判断也就成了死代码）
                foreach (var method in MisplacedMethods(type))
                {
                    ValidateMethod(type, method);
                }

                // ④ 类级特性：与方法级同一把尺子。标了就必须能拦到，否则启动失败。
                //    Enabled = false 是明确的「这个类不要事务」—— 派生类去掉基类继承来的特性只有这一个办法
                //    （AttributeUsage 是 Inherited = true），所以它必须真的豁免，不能标了反而启动失败。
                if (classLevel is { Enabled: true })
                {
                    methodCount += CollectClassLevelAsyncMethods(type, requireVirtual: classProxy);
                }

                intercepted.Add(type);
            }

            // ⑤ 特性标在「扫描注册够不到」的类型上 —— 最隐蔽的一种失效：能编译、能跑、就是没事务。
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
            var where = Where(type, method);

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
                $"[VivUnitOfWork] 无业务接口的类型走类代理，只能拦 public virtual 方法：{Where(type, method)}。" +
                "非 virtual 或 sealed override 都拦不到，请加 virtual，或为它定义接口。");
        }

        /// <summary>
        /// 接口代理只按接口调用（<c>AsImplementedInterfaces()</c>，类型本身不注册 AsSelf）——
        /// 标了特性的方法不在任何接口的映射里，容器里根本调不到它，事务一定开不起来。
        /// 这是最像「生效了」的一种失效：注册期不报错、代理也挂上了，只有那个方法永远没人走。
        /// </summary>
        private static void RequireInterfaceMethod(Type type, MethodInfo method)
        {
            foreach (var contract in type.GetInterfaces())
            {
                foreach (var target in type.GetInterfaceMap(contract).TargetMethods)
                {
                    // 继承实现的 TargetMethods 给的是基类那份，用 GetBaseDefinition 归一化后再比
                    if (target == method || target.GetBaseDefinition() == method.GetBaseDefinition()) return;
                }
            }

            throw new InvalidOperationException(
                $"[VivUnitOfWork] {Where(type, method)} 标了特性，但它不是任何业务接口的成员 —— " +
                "该类型有业务接口，只能走接口代理（只按接口注册，类型本身不注册 AsSelf），" +
                "这个方法从容器里根本调不到，事务不会生效。请把它加进业务接口（特性仍标在实现类上），" +
                "或去掉特性。");
        }

        /// <summary>
        /// 类级特性下，每个公开实例方法都必须能被拦截（与方法级同一套 ValidateMethod），
        /// 否则「标了特性 = 有事务」不成立。方法上显式 <c>Enabled = false</c> 的跳过。
        ///
        /// <para>方法级（含从基类继承来的）优先于类级 —— 与运行期 ShouldIntercept 的取值顺序一致，
        /// 所以这里的跳过判定也要用 <c>inherit: true</c>：派生类重写基类那个带特性的方法时，
        /// 运行期拿到的是方法级特性，类级不该再校验一遍。</para>
        ///
        /// <para>类代理下额外要求 public virtual。注意：类代理会拦 <c>this.VirtualMethod()</c>
        /// （虚派发），自调用会再开一层嵌套事务 —— 现有嵌套语义是子句柄、提交 no-op，安全。</para>
        ///
        /// <para>只扫本类声明的方法，不是取舍，是与运行期一致：ShouldIntercept 找不到方法级特性时
        /// 回落到 <c>method.DeclaringType</c> 上的类级特性，继承来的方法 DeclaringType 是基类，
        /// 根本读不到派生类的类级事务。要让基类方法也开事务，就在基类上标方法级特性。</para>
        ///
        /// <para>属性存取器（<c>IsSpecialName</c>）跳过：存取器是同步方法，异步拦截链一定管不了，
        /// 校验它们只会让「带 public 属性的类级事务」全部启动失败。</para>
        /// </summary>
        private static int CollectClassLevelAsyncMethods(Type type, bool requireVirtual)
        {
            var count = 0;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName) continue;              // 属性存取器不单独计数

                var methodAttribute = method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true);
                if (methodAttribute is not null) continue;       // 方法级（含继承来的）已经校验过

                ValidateMethod(type, method);
                if (requireVirtual) RequireVirtual(type, method);
                else RequireInterfaceMethod(type, method);
                count++;
            }

            return count;
        }

        private static bool HasAttribute(Type type)
        {
            if (type.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true) != null) return true;

            // 会被代理命中的方法：继承链上的 public 实例方法，特性沿 override 链找。
            // 继承来的方法（派生类没重写）也必须算进来 —— 漏了它类型就不进拦截清单，
            // 运行期连拦截器都不挂，基类上标的那个事务静默失效。
            if (GetInterceptableMethods(type)
                .Any(m => m.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: true) != null))
            {
                return true;
            }

            // 标在非 public / static 方法上 —— 拦不到，但也不能当没看见，交给主循环报错
            return MisplacedMethods(type).Any();
        }

        /// <summary>
        /// 会真正被代理命中的方法集：继承链上的 public 实例方法。override 在结果里只保留最派生的
        /// 那一个 —— 正好是运行期 <c>MethodInvocationTarget</c> 指向的方法，特性也从它往基类找。
        /// </summary>
        private static IEnumerable<MethodInfo> GetInterceptableMethods(Type type)
            => type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// 标了特性但代理一定拦不到的声明方法：非 public 或 static。只扫本类声明 ——
        /// 继承来的 private 不可见，static 本来也不参与代理。这个集合只用来报错。
        /// </summary>
        private static IEnumerable<MethodInfo> MisplacedMethods(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static
                                                   | BindingFlags.DeclaredOnly))
            {
                if (method.IsPublic && !method.IsStatic) continue;   // 有效方法集那趟已经覆盖

                if (method.GetCustomAttribute<VivUnitOfWorkAttribute>(inherit: false) is { Enabled: true })
                {
                    yield return method;
                }
            }
        }

        /// <summary>
        /// 报错定位。方法是从基类继承来的时候把声明类型也带上 —— 要补 virtual 的是基类那一份。
        /// </summary>
        private static string Where(Type type, MethodInfo method)
            => method.DeclaringType is null || method.DeclaringType == type
                ? $"{type.FullName}.{method.Name}"
                : $"{type.FullName}.{method.Name}（声明在 {method.DeclaringType.FullName}）";

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
