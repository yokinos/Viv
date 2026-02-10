using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;
using Viv.Nana.Models;

namespace Viv.Nana.LocalMessage
{
    public interface ILocalMessageRespository
    {
        Task<bool> AddMessageAsync<T>(NanaMessage<T> message) where T : VivMessage;
    }
}
