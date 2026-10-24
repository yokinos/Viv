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

        public MomoDbContextFactory(IVivContext vivContext, ILoggerContract logger)
        {
            _vivContext = vivContext;
            _logger = logger;
        }

        /// <inheritdoc/>
        public IMomoDbContext? Create(DatabaseOptions options)
        {
            if (options == null) return null;
            return new MomoDatabaseContext(_vivContext, _logger, new FixedOptionsProvider(options));
        }

        private sealed class FixedOptionsProvider : IDatabaseOptionsProvider
        {
            private readonly DatabaseOptions _options;

            public FixedOptionsProvider(DatabaseOptions options) => _options = options;

            public DatabaseOptions GetRealOptions() => _options;
        }
    }
}
