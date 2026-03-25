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
            var copy = options.DeepCopy();
            ArgumentNullException.ThrowIfNull(copy);
            VivConfigRegistry.Add(copy);
        }
    }
}
