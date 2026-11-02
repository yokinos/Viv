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
    /// 无接口类型的 sealed / 非 public / 非 virtual 方法，特性标在接口上，标在消费者上，开放泛型，
    /// 以及没配 Redis。逐条报错并给出处理建议。接口代理下实现方法不要求 virtual；
    /// 类代理下必须是 public virtual。</para>
    /// </summary>
    internal static class DistributedLockRegistration
    {
        public static Type[] Resolve(
            IEnumerable<Type> scannedTypes,
            IEnumerable<Type> dependencyTypes,
            bool lockEnabled)
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

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Instance | BindingFlags.Static
                                                       | BindingFlags.DeclaredOnly))
                {
                    var methodAttribute = method.GetCustomAttribute<VivDistributedLockAttribute>(inherit: false);
                    if (methodAttribute is null || !methodAttribute.Enabled) continue;

                    ValidateMethod(type, method, methodAttribute);
                    if (classProxy) RequireVirtual(type, method);
                }

                if (classLevel is { Enabled: true })
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                                                           | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsSpecialName) continue;

                        // 方法上有自己的特性（含 Enabled = false 的显式豁免）时以方法级为准
                        var methodAttribute = method.GetCustomAttribute<VivDistributedLockAttribute>(inherit: false);
                        if (methodAttribute is not null) continue;

                        ValidateMethod(type, method, classLevel);
                        if (classProxy) RequireVirtual(type, method);
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
                $"[VivDistributedLock] 无业务接口的类型走类代理，只能拦 public virtual 方法：{type.FullName}.{method.Name}。" +
                "非 virtual 或 sealed override 都拦不到，请加 virtual，或为它定义接口。");
        }

        private static void ValidateMethod(Type type, MethodInfo method, VivDistributedLockAttribute attribute)
        {
            var where = $"{type.FullName}.{method.Name}";

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
                    throw new InvalidOperationException(
                        $"[VivDistributedLock] {where} 同时配置了 KeyTemplate 和 ILockKey 参数，二选一。");
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

            throw new InvalidOperationException(
                $"[VivDistributedLock] {where} 没有可用的锁 Key：请配置 KeyTemplate、让唯一参数实现 ILockKey，或显式 Global = true。");
        }

        private static bool HasAttribute(Type type)
        {
            if (type.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true) != null) return true;

            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                       .Any(m => m.GetCustomAttribute<VivDistributedLockAttribute>(inherit: true) != null);
        }

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
