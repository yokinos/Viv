using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Log;

namespace Viv.Nana
{
    /// <summary>
    /// VivConsumer 所依赖的注入
    /// </summary>
    [VivDependency]
    public class VivConsumerDependency : IDependency
    {
        public readonly ILoggerContract _logger;

        public readonly IVivContext _context;

        public readonly IVivEventPublisher _publisher;

        /// <summary>
        /// 消费锁。未配 Redis 时容器可能解析不到，允许为 null——此时 <see cref="VivConsumer{T}"/> 跳过取锁。
        /// </summary>
        public readonly IDistributedLock? _distributedLock;

        public VivConsumerDependency(
            ILoggerContract logger,
            IVivContext context,
            IVivEventPublisher publisher,
            IDistributedLock? distributedLock = null)
        {
            _logger = logger;
            _context = context;
            _publisher = publisher;
            _distributedLock = distributedLock;
        }
    }
}
