using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Apex.Core.Entity.Dto.Account.Output
{
    public class ApexLoginOutput
    {
        public string AccessToken { get; set; }

        public string RefreshToken { get; set; }

        public DateTime AccessTokenExpires { get; set; }

        public DateTime RefreshTokenExpires { get; set; }

        public string? Name { get; set; }

        public string? NickName { get; set; }

        public long? UserId { get; set; }

        public string? Phone { get; set; }

        public string? AvatarUrl { get; set; }
    }
}
