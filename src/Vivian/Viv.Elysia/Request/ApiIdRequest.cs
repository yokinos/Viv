using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Elysia.Request
{
    public class ApiIdRequest : ApiRequestBase
    {
        public long Id { get; set; }

        public override string Validate()
        {
            if (Id <= 0)
            {
                return "参数错误";
            }

            return base.Validate();
        }
    }
}
