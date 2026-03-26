using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Options;
using Viv.Vva;
using Viv.Vva.Extension;

namespace Viv.Momo
{
    public class MomoRegister
    {
        public static void Initialize(DatabaseOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            VivConfigRegistry.Add(options);
        }
    }
}
