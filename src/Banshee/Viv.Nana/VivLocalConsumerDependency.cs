using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Nana
{
    /// <summary>
    /// VivLocalConsumer 所依赖的注入。
    ///
    /// 刻意**不复用** <see cref="VivConsumerDependency"/> —— 那个包捆着
    /// <see cref="IVivEventPublisher"/>（跨进程），复用即把 Nana 那条线的耦合带进本地队列。
    ///
    /// 也比它少了两个字段：
    /// - <c>IDistributedLock?</c>：本地队列就在本进程，不存在 fanout 多实例竞争，不需要消费锁
    /// - <c>NanaOptions</c>：本版没有延迟重投上限要读
    ///
    /// 将来补 <c>VivLocalConsumer.RedeliverAsync</c> 时往这里加字段即可 ——
    /// 业务子类 <c>: base(dependency)</c> 的签名不变，不破坏既有代码。
    /// </summary>
    [VivDependency]
    public class VivLocalConsumerDependency : IDependency
    {
        public readonly ILoggerContract _logger;

        public readonly IVivContext _context;

        public readonly IVivLocalEventPublisher _publisher;

        public VivLocalConsumerDependency(
            ILoggerContract logger,
            IVivContext context,
            IVivLocalEventPublisher publisher)
        {
            _logger = logger;
            _context = context;
            _publisher = publisher;
        }
    }
}
