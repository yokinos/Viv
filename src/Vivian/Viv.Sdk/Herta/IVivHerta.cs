using System;
using System.Collections.Generic;
using System.Text;
using Viv.Sdk.Herta.Response;

namespace Viv.Sdk.Herta
{
    public interface IVivHerta
    {
        public Task<HertaLoginResponse> LoginAsync(long tenantId, string loginCode, string password);

    }
}
