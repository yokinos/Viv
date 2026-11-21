using System.Reflection;
using System.Threading.Tasks;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Nana;

namespace Viv.Engine.Locking
{
    /// <summary>
    /// 分布式锁的注册期筛选与启动期校验。
    ///
    /// <para>与工作单元同一套逻辑：有业务接口的类型走接口代理，没有业务接口的走类代理。
    /// 锁没生效是无声的，所以凡是能静态判定的失效原因都前移到启动期硬报错。</para>
    ///
    /// <para><b>Key 校验</b></para>
    /// <list type="bullet">
    /// <item><description><c>KeyTemplate</c>：占位符必须对得上参数名/上下文 token，属性路径必须可读且花括号配对</description></item>
    /// <item><description><c>ILockKey</c>：方法参数里必须恰好一个实现它（0 个 / 多个都失败）</description></item>
    /// <item><description><c>Global</c>：不能再配 KeyTemplate 或 ILockKey 参数</description></item>
    /// <item><description>三者都没有 → 启动失败，不允许静默不加锁</description></item>
    /// </list>
    ///
    /// <para><b>拦不到的情况</b>：非 public / static / 泛型 / 同步 / ValueTask 方法，
    /// 无接口类型的 sealed / 非 public / 非 virtual 方法，有接口但方法不是任何接口成员的 public 方法，
    /// 特性标在接口上，标在消费者上，开放泛型，以及没配 Redis。逐条报错并给出处理建议。
    /// 接口代理下实现方法不要求 virtual；类代理下必须是 public virtual。</para>
    ///
    /// <para><b>继承</b>：方法级特性沿基类链生效 —— 基类方法标了特性，派生类重写（自己不标）或原样继承，
    /// 都按基类那一条校验，和运行期查特性的方式（<c>inherit: true</c>）保持一致。
    /// 派生类要豁免只能显式标 <c>Enabled = false</c>。类级特性相反，只作用于本类声明的方法：
    /// 运行期找不到方法级特性时会回落到 <c>method.DeclaringType</c> 上的类级特性，而继承来的方法
    /// 其 DeclaringType 是基类 —— 派生类的类级锁本来就不覆盖它们，注册期也就不校验。</para>
    /// </summary>
    internal static class DistributedLockRegistration
    {
        public static Type[] Resolve(IEnumerable<Type> scannedTypes, IEnumerable<Type> dependencyTypes, bool lockEnabled)
        {
            var registered = scannedTypes
                .Where(t => !t.IsGenericTypeDefinition && !t.IsAbstract)
                .Distinct()
                .ToArray();

            var registeredSet = new HashSet<Type>(registered);

            var attributedOpenGenerics = scannedTypes
                .Concat(dependencyTypes)
                .Where(t => t.IsGenericTypeDefinition && HasAttribute(t))
                .Distinct()
                .ToArray();

            if (attributedOpenGenerics.Length > 0)
            {
                throw new InvalidOperationException(
                    "[VivDistributedLock] 标在开放泛型类型上，扫描注册不会为开放泛型挂代理，锁不会生效：" +
                    string.Join(", ", attributedOpenGenerics.Select(t => t.FullName)) +
                    "。请把特性标在闭合实现上。");
            }

            var anyoneAttributed = registered.Concat(dependencyTypes).Any(HasAttribute);
            if (anyoneAttributed && !lockEnabled)
            {
                throw new InvalidOperationException(
                    "检测到 [VivDistributedLock]，但 VivOptions.CacheOption 未启用 Redis" +
                    "（CacheProviderType 不是 Redis）—— 分布式锁依赖 IDistributedLock，请先配好 Redis。");
            }

            var intercepted = new List<Type>();

            foreach (var type in registered)
            {
                // 特性标在接口方法上时，接口代理的 invocation.MethodInvocationTarget 是实现方法，
                // 运行期查不到特性 —— 静默无锁。这里直接挡掉。
                var interfaceAttributed = type.GetInterfaces()
                    .SelectMany(i => i.GetMethods())
                    .Any(m => m.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true) != null);

                if (interfaceAttributed)
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] {type.FullName} 的接口上标了特性，但接口代理只认实现类上的特性，" +
                        "锁不会生效。请把特性标在实现类的 public virtual 方法上。");
                }

                if (!HasAttribute(type)) continue;

                if (IsConsumer(type))
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] 标在消费者 {type.FullName} 上，但消费者不走接口代理，锁不会生效。" +
                        "请把锁放在它调用的 Service 方法上，或用 GetConsumeLockKey 定制消费锁。");
                }

                // 没有业务接口 → Castle 类代理。类代理生成子类，所以类型必须 public、非 sealed、
                // 有 public 构造；被拦方法必须 public virtual。任何一条不满足都是静默无锁，启动期硬报错。
                var classProxy = ProxyPolicy.NeedsClassProxy(type);

                if (classProxy)
                {
                    ValidateClassProxyType(type);
                }

                var classLevel = type.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true);

                // 方法级：枚举「会真正被代理命中的方法集」—— 继承链上的 public 实例方法
                // （override 只留最派生的那一个，正是运行期 MethodInvocationTarget 指向的方法），
                // 特性沿 override 链找，与运行期 ResolveAttribute 的 inherit: true 对齐。
                // 只用 DeclaredOnly + inherit: false 的话：「基类标了、子类重写不标」整条跳过校验，
                // 「基类标了、子类不重写」连 HasAttribute 都发现不了 —— 类型不进拦截清单，锁静默失效。
                //
                // 这里不跳 IsSpecialName（类级循环要跳）：显式标在属性访问器上的锁一定生效不了 ——
                // 存取器是同步方法，异步拦截链管不了它 —— 必须报错，不能像类级那样放行。
                foreach (var method in GetInterceptableMethods(type))
                {
                    var methodAttribute = method.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true);
                    if (methodAttribute is null || !methodAttribute.Enabled) continue;

                    ValidateMethod(type, method, methodAttribute);
                    if (classProxy) RequireVirtual(type, method);
                    else RequireInterfaceMethod(type, method);
                }

                // 标在非 public / static 方法上：代理一定拦不到，也不在上面那趟有效方法集里，
                // 单独扫一遍让它们报错，而不是被静默忽略
                foreach (var (method, misplaced) in MisplacedMethods(type))
                {
                    ValidateMethod(type, method, misplaced);
                }

                if (classLevel is { Enabled: true })
                {
                    // 只扫本类声明的方法 —— 不是取舍，是与运行期一致：ResolveAttribute 找不到方法级特性时
                    // 回落到 method.DeclaringType 上的类级特性，继承来的方法 DeclaringType 是基类，
                    // 根本读不到派生类的类级锁。要让基类方法也上锁，就得在基类上标方法级特性。
                    //
                    // Accessor（IsSpecialName）跳过：存取器是同步方法，异步拦截链一定管不了，
                    // 校验它们只会让「带 public 属性的类级锁」全部启动失败。
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                                           | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsSpecialName) continue;

                        // 方法级（含从基类继承来的）优先于类级 —— 与运行期 ResolveAttribute 的取值顺序一致；
                        // 标了 Enabled = false 的同样在此豁免
                        if (method.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true) is not null) continue;

                        ValidateMethod(type, method, classLevel);
                        if (classProxy) RequireVirtual(type, method);
                        else RequireInterfaceMethod(type, method);
                    }
                }

                intercepted.Add(type);
            }

            foreach (var type in dependencyTypes.Distinct())
            {
                if (registeredSet.Contains(type)) continue;
                if (!HasAttribute(type)) continue;

                if (IsConsumer(type))
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] 标在消费者 {type.FullName} 上，但消费者不走接口代理，锁不会生效。" +
                        "请把锁放在它调用的 Service 方法上。");
                }

                throw new InvalidOperationException(
                    $"[VivDistributedLock] 标在 {type.FullName} 上，但该类型不是由 DIOption 扫描注册的" +
                    "（可能标了 [VivDependency(AsSelf = true)]，或不在 DIOption 扫描范围内）—— " +
                    "代理挂不上去，锁不会生效。请把它移进 ServiceImplementation / RepositoryImplementation " +
                    "的扫描范围，或为它定义接口后按接口注册。");
            }

            return intercepted.ToArray();
        }

        /// <summary>
        /// 无业务接口的类型只能走 Castle 类代理 —— 生成子类，所以类型必须 public、非 sealed，
        /// 且有 public 构造函数。任何一条不满足，代理都根本生成不出来。
        /// </summary>
        private static void ValidateClassProxyType(Type type)
        {
            if (!type.IsVisible)
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] {type.FullName} 没有业务接口，只能走类代理，但该类型不是 public —— " +
                    "Castle 无法在动态程序集里继承它。请把类型改成 public（internal 还要给业务程序集加 " +
                    "InternalsVisibleTo(\"DynamicProxyGenAssembly2\")，本仓没有这条约定），或为它定义接口。");
            }

            if (type.IsSealed)
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] {type.FullName} 没有业务接口，只能走类代理，但该类型是 sealed —— " +
                    "Castle 生成不出子类。请去掉 sealed，或为它定义接口。");
            }

            if (type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0)
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] {type.FullName} 没有业务接口，只能走类代理，但没有 public 构造函数 —— " +
                    "Autofac 选不到构造，代理生成不出来。请补一个 public 构造函数，或为它定义接口。");
            }
        }

        /// <summary>类代理只能拦 public virtual 方法；非 virtual 或 sealed override 都是静默无锁。</summary>
        private static void RequireVirtual(Type type, MethodInfo method)
        {
            if (method.IsVirtual && !method.IsFinal) return;

            throw new InvalidOperationException(
                $"[VivDistributedLock] 无业务接口的类型走类代理，只能拦 public virtual 方法：{Where(type, method)}。" +
                "非 virtual 或 sealed override 都拦不到，请加 virtual，或为它定义接口。");
        }

        /// <summary>
        /// 接口代理只按接口调用（<c>AsImplementedInterfaces()</c>，类型本身不注册 AsSelf）——
        /// 标了特性的方法不在任何接口的映射里，容器里根本调不到它，锁一定不生效。
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
                $"[VivDistributedLock] {Where(type, method)} 标了特性，但它不是任何业务接口的成员 —— " +
                "该类型有业务接口，只能走接口代理（只按接口注册，类型本身不注册 AsSelf），" +
                "这个方法从容器里根本调不到，锁不会生效。请把它加进业务接口（特性仍标在实现类上），" +
                "或去掉特性。");
        }

        private static void ValidateMethod(Type type, MethodInfo method, VivDistributedLockAttribute attribute)
        {
            var where = Where(type, method);

            if (!method.IsPublic)
                throw new InvalidOperationException($"[VivDistributedLock] 标在非 public 方法上，代理拦不到：{where}");

            if (method.IsStatic)
                throw new InvalidOperationException($"[VivDistributedLock] 标在静态方法上，代理拦不到：{where}");

            // 接口代理（CreateInterfaceProxyWithTarget）按接口调用、反射转给目标实例，
            // 实现方法不需要 virtual —— 非 virtual 也能拦到（实测）。类代理则必须 public virtual，
            // 由 RequireVirtual 单独校验；这里只拦静态 / 同步 / 泛型 / ValueTask。

            if (method.IsGenericMethodDefinition)
                throw new InvalidOperationException($"[VivDistributedLock] 标在泛型方法上，代理拦不到：{where}");

            if (method.ReturnType == typeof(ValueTask)
                || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] 标在返回 ValueTask / ValueTask<T> 的方法上：{where}。" +
                    "异步拦截链不能安全拦截 ValueTask，请改为返回 Task / Task<T>。");
            }

            if (!IsAsyncReturn(method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] 标在同步方法上：{where} 返回 {method.ReturnType.Name}。" +
                    "请改为返回 Task / Task<T>。");
            }

            ValidateKey(method, attribute, where);
        }

        private static void ValidateKey(MethodInfo method, VivDistributedLockAttribute attribute, string where)
        {
            var parameters = method.GetParameters();
            var lockKeyParameters = parameters
                .Where(p => typeof(ILockKey).IsAssignableFrom(p.ParameterType))
                .ToArray();

            if (attribute.Global)
            {
                if (!string.IsNullOrWhiteSpace(attribute.KeyTemplate))
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] {where} 同时配置了 Global 和 KeyTemplate，二选一。");
                }

                if (lockKeyParameters.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] {where} 同时配置了 Global 和 ILockKey 参数，二选一。");
                }

                return;
            }

            if (!string.IsNullOrWhiteSpace(attribute.KeyTemplate))
            {
                if (lockKeyParameters.Length > 0)
                {
                    throw new InvalidOperationException($"[VivDistributedLock] {where} 同时配置了 KeyTemplate 和 ILockKey 参数，二选一。");
                }

                LockKeyTemplate.Validate(attribute.KeyTemplate, parameters, where);
                return;
            }

            if (lockKeyParameters.Length == 1) return;

            if (lockKeyParameters.Length > 1)
            {
                throw new InvalidOperationException(
                    $"[VivDistributedLock] {where} 有多个参数实现了 ILockKey，无法确定用哪个：{string.Join(", ", lockKeyParameters.Select(p => p.Name))}。");
            }

            throw new InvalidOperationException($"[VivDistributedLock] {where} 没有可用的锁 Key：请配置 KeyTemplate、让唯一参数实现 ILockKey，或显式 Global = true。");
        }

        private static bool HasAttribute(Type type)
        {
            if (type.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true) != null) return true;

            // 会被代理命中的方法：继承链上的 public 实例方法，特性沿 override 链找。
            // 继承来的方法（派生类没重写）也必须算进来 —— 漏了它类型就不进拦截清单，
            // 运行期连拦截器都不挂，基类上标的那把锁静默失效。
            if (GetInterceptableMethods(type)
                .Any(m => m.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true) != null))
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
        private static IEnumerable<(MethodInfo Method, VivDistributedLockAttribute Attribute)> MisplacedMethods(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static
                                                   | BindingFlags.DeclaredOnly))
            {
                if (method.IsPublic && !method.IsStatic) continue;   // 有效方法集那趟已经覆盖

                var attribute = method.GetCustomAttribute<VivDistributedLockAttribute>(inherit: false);
                if (attribute is { Enabled: true })
                {
                    yield return (method, attribute);
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

        /// <summary>消费者由基类显式管理消费锁，不走接口代理，不能用这个特性。</summary>
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

        private static bool IsAsyncReturn(Type returnType)
        {
            if (returnType == typeof(Task)) return true;
            if (!returnType.IsGenericType) return false;

            return returnType.GetGenericTypeDefinition() == typeof(Task<>);
        }
    }
}
