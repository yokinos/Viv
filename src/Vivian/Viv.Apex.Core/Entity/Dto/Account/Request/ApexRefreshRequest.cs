using System;
using System.Collections.Generic;
using System.Text;
using Viv.Elysia.Request;

namespace Viv.Apex.Core.Entity.Dto.Account.Request
{
    public class ApexRefreshRequest : ApiRequestBase
    {
        public long UserId { get; set; }

        public string RefreshToken { get; set; }
    }
}
