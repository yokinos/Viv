using System.Reflection;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.Extensions.DependencyInjection;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Saga 反射注册辅助 — 动态注册 MassTransit StateMachine + EF Core 持久化
    /// </summary>
    internal static class VivSagaRegistrationHelper
    {
        /// <summary>
        /// 从 MassTransitStateMachine&lt;TState&gt; 中提取 TState 类型
        /// </summary>
        public static Type? ExtractStateType(Type stateMachineType)
        {
            var baseType = stateMachineType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(MassTransitStateMachine<>))
                    return baseType.GetGenericArguments()[0];
                baseType = baseType.BaseType;
            }
            return null;
        }

        /// <summary>
        /// 注册单个 Saga（等价于 x.AddSagaStateMachine+TStateMachine,TState>().EntityFrameworkRepository(...)）
        /// </summary>
        public static void RegisterSaga(IBusRegistrationConfigurator busConfig, Type stateMachineType, Type stateType)
        {
            // 遍历 MassTransit 程序集中所有 public static 方法，找到 AddSagaStateMachine<TMachine, TState>
            var massTransitAsm = typeof(IBusRegistrationConfigurator).Assembly;
            var addMethod = FindMethod(massTransitAsm, "AddSagaStateMachine", genericArgCount: 2, paramCount: 1);
            if (addMethod == null) return;

            var genericAdd = addMethod.MakeGenericMethod(stateMachineType, stateType);
            var sagaConfigurator = genericAdd.Invoke(null, [busConfig]);
            if (sagaConfigurator == null) return;

            // 遍历 MassTransit.EntityFrameworkCore 程序集中所有 public static 方法，找到 EntityFrameworkRepository<T>
            var efAsm = typeof(SagaDbContext).Assembly;
            var efRepoMethod = FindMethod(efAsm, "EntityFrameworkRepository", genericArgCount: 1, paramCount: 1);
            if (efRepoMethod == null) return;

            var genericEfRepo = efRepoMethod.MakeGenericMethod(stateType);
            genericEfRepo.Invoke(null, [sagaConfigurator]);
        }

        private static MethodInfo? FindMethod(Assembly assembly, string methodName, int genericArgCount, int paramCount)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == methodName
                        && method.GetGenericArguments().Length == genericArgCount
                        && method.GetParameters().Length == paramCount)
                    {
                        return method;
                    }
                }
            }
            return null;
        }
    }
}
