using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Options;

namespace Viv.Momo.Interface
{
    public interface IMomoDbContextFactory
    {
        /// <summary>
        /// Creates a new instance of IMomoDbContext based on the provided DatabaseOptions.
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        IMomoDbContext? Create(DatabaseOptions options);
    }
}
