using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Nana
{
    public interface IVivConsumer
    {
        Task SubscribeAsync(CancellationToken cancellationToken = default);
    }
}
 