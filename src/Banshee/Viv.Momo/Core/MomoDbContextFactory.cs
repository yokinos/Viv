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

        private readonly IDatabaseOptionsProvider _optionsProvider;

        public MomoDbContextFactory(IVivContext vivContext, ILoggerContract logger, IDatabaseOptionsProvider optionsProvider)
        {
            _vivContext = vivContext;
            _logger = logger;
            _optionsProvider = optionsProvider;
        }

        /// <inheritdoc/>
        public IMomoDbContext? Create(DatabaseOptions options)
        {
            if (options == null) return null;
            var dataContext = new MomoDatabaseContext(_vivContext, _logger, _optionsProvider);
            dataContext.SetOptions(options);
            return dataContext;
        }
    }
}
