using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Entity.Any
{
    public class RefreshTokenValue
    {
        public long AppId { get; set; }

        public long UserId { get; set; }

        public string RefreshToken { get; set; } = string.Empty;
    }
}
