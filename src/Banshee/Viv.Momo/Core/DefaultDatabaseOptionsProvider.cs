using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Interface;
using Viv.Momo.Options;

namespace Viv.Momo.Core
{
    public class DefaultDatabaseOptionsProvider : IDatabaseOptionsProvider
    {
        public DatabaseOptions GetOptions(DatabaseOptions defaultOptions)
        {
            return defaultOptions;
        }
    }
}
