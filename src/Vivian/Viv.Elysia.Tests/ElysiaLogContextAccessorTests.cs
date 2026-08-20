using Viv.Entity.Enums;

namespace Viv.Elysia.Tests
{
    /// <summary>
    /// ElysiaLogContextAccessor 的「预置可变容器」语义。
    /// 回归点：AsyncLocal 只从父流向子——若 OperationLogFilter 不预置容器，
    /// action 里 SetLog 的写入跨 await 流不回 filter 续段，Current 恒为 null。
    /// </summary>
    public class ElysiaLogContextAccessorTests
    {
        [Fact]
        public async Task 预置容器后_action异步里SetLog_父await后能读到()
        {
            ElysiaLogContextAccessor.Clear();
            ElysiaLogContextAccessor.Set(new OperationLogContext()); // filter 预置

            await SimulateActionAsync();

            var ctx = ElysiaLogContextAccessor.Current;
            Assert.NotNull(ctx);
            Assert.True(ctx.IsSet);
            Assert.Equal(EmOperationModule.User, ctx.Module);
            Assert.Equal(EmOperationType.Login, ctx.Operation);
        }

        [Fact]
        public async Task 不预置容器_action异步里SetLog_父await后读不到()
        {
            ElysiaLogContextAccessor.Clear();

            await SimulateActionAsync();

            // 旧行为：子任务里的 AsyncLocal 写入跨 await 流不回父 → 必须由 filter 预置容器
            Assert.Null(ElysiaLogContextAccessor.Current);
        }

        [Fact]
        public void 无预置容器SetLog_创建独立上下文_IsSet为true()
        {
            ElysiaLogContextAccessor.Clear();

            ElysiaLogContextAccessor.SetLog(EmOperationModule.User, EmOperationType.Login, isRecord: false);

            var ctx = ElysiaLogContextAccessor.Current;
            Assert.NotNull(ctx);
            Assert.True(ctx.IsSet);
            Assert.False(ctx.IsRecord);
            Assert.Equal(EmOperationModule.User, ctx.Module);
        }

        [Fact]
        public void 预置容器但未SetLog_IsSet为false()
        {
            ElysiaLogContextAccessor.Clear();
            ElysiaLogContextAccessor.Set(new OperationLogContext());

            Assert.NotNull(ElysiaLogContextAccessor.Current);
            Assert.False(ElysiaLogContextAccessor.Current!.IsSet);
        }

        [Fact]
        public void Clear_清空当前值()
        {
            ElysiaLogContextAccessor.Set(new OperationLogContext());
            ElysiaLogContextAccessor.Clear();

            Assert.Null(ElysiaLogContextAccessor.Current);
        }

        private static async Task SimulateActionAsync()
        {
            await Task.Yield(); // 模拟 action 首个 await
            ElysiaLogContextAccessor.SetLog(EmOperationModule.User, EmOperationType.Login);
            await Task.Yield(); // 模拟 action 后续 await
        }
    }
}
