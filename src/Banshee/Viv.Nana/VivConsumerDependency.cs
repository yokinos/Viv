using System;
using System.Collections.Generic;
using System.Text;
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

        public VivConsumerDependency(ILoggerContract logger, IVivContext context, IVivEventPublisher publisher)
        {
            _logger = logger;
            _context = context;
            _publisher = publisher;
        }
    }
}
