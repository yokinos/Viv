using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine;
using Viv.Herta.Core.Entity.ViewModel.Chat;

namespace Viv.Herta.Core.IService
{
    public interface IChatService
    {
        Task<VivApiResult> SendMessageAsync(SendMessageRequest request);
    }
}
