using Microsoft.Extensions.Options;
using Viv.Contracts.Attributes;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Nana.Options;

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

        public readonly NanaOptions _nanaOptions;

        /// <summary>
        /// 本地事件总线。消费成功时由 <see cref="VivConsumer{T}.HandleAsync"/> 分发本次消费入队的事件，
        /// 失败则整队丢弃。必填不给默认值 —— 它总是注册的，「忘了传」若静默通过就成了事件不发。
        /// </summary>
        public readonly IVivLocalEventBus _localEventBus;

        /// <summary>
        /// 工作单元。未配数据库时容器可能解析不到，允许为 null；
        /// 子类标了 <c>[VivUnitOfWork]</c> 却为 null 时，基类构造会立刻失败。
        /// </summary>
        public readonly IVivUnitOfWork? _unitOfWork;

        public VivConsumerDependency(
            ILoggerContract logger,
            IVivContext context,
            IVivEventPublisher publisher,
            IOptions<NanaOptions> options,
            IVivLocalEventBus localEventBus,
            IDistributedLock? distributedLock = null,
            IVivUnitOfWork? unitOfWork = null)
        {
            _logger = logger;
            _context = context;
            _publisher = publisher;
            _nanaOptions = options.Value;
            _localEventBus = localEventBus;
            _distributedLock = distributedLock;
            _unitOfWork = unitOfWork;
        }
    }
}
