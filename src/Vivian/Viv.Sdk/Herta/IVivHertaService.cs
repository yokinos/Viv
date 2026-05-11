using System;
using System.Collections.Generic;
using System.Text;
using Viv.Sdk.Herta.Response;

namespace Viv.Sdk.Herta
{
    public interface IVivHertaService
    {
        public Task<HertaLoginResponse> LoginAsync(long tenantId, string loginCode, string password);

        public Task<SendMessageResponse> SendMessageAsync();
    }
}
