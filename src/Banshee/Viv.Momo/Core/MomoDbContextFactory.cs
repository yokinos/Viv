using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Interface;
using Viv.Log;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Core
{
    public class MomoDbContextFactory : IMomoDbContextFactory
    {
        private readonly IVivContext _vivContext;

        private readonly ILoggerContract _logger;

        private readonly ITransactionAttemptListener? _attemptListener;

        /// <summary>
        /// 事务提交观察者
        /// </summary>
        private readonly IEnumerable<ITransactionObserver>? _observers;

        public MomoDbContextFactory(IVivContext vivContext, ILoggerContract logger, ITransactionAttemptListener? attemptListener = null, IEnumerable<ITransactionObserver>? observers = null)
        {
            _vivContext = vivContext;
            _logger = logger;
            _attemptListener = attemptListener;
            _observers = observers;
        }

        /// <inheritdoc/>
        public IMomoDbContext? Create(DatabaseOptions options)
        {
            if (options == null) return null;
            return new MomoDatabaseContext(_vivContext, _logger, new FixedOptionsProvider(options), _attemptListener, _observers);
        }

        private sealed class FixedOptionsProvider : IDatabaseOptionsProvider
        {
            private readonly DatabaseOptions _options;

            public FixedOptionsProvider(DatabaseOptions options) => _options = options;

            public DatabaseOptions GetRealOptions() => _options;
        }
    }
}
