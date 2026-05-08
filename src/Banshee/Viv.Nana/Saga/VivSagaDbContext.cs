using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace Viv.Nana.Saga
{
    /// <summary>
    /// Viv Saga 持久化上下文 — 所有业务 Saga 状态统一存于此
    /// 通过 DI 注入 <see cref="ISagaClassMap"/> 自动发现业务 Saga 映射
    /// </summary>
    public class VivSagaDbContext : SagaDbContext
    {
        private readonly ISagaClassMap[] _sagaMaps;

        public VivSagaDbContext(DbContextOptions<VivSagaDbContext> options, IEnumerable<ISagaClassMap> sagaMaps)
            : base(options)
        {
            _sagaMaps = sagaMaps?.ToArray() ?? [];
        }

        protected override IEnumerable<ISagaClassMap> Configurations => _sagaMaps;
    }
}
