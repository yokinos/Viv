using System;
using System.Collections.Generic;
using System.Text;
using Viv.Engine.Http;
using Viv.Sdk.Herta.Response;

namespace Viv.Sdk.Herta
{
    /// <summary>
    /// Viv 黑塔 Chat 服务
    /// 提供给内部服务调用的模块
    /// </summary>
    public class HertaService: IVivHertaService
    {
        public HertaService(IVivHttpService httpService) { }

        public Task<HertaLoginResponse> LoginAsync(long tenantId, string loginCode, string password)
        {
            throw new NotImplementedException();
        }

        public Task<SendMessageResponse> SendMessageAsync()
        {
            throw new NotImplementedException();
        }
    }
}
