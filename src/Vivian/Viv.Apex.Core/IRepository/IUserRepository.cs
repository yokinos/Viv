using System;
using System.Collections.Generic;
using System.Text;
using Viv.Entity.Database.Apex;

namespace Viv.Apex.Core.IRepository
{
    public interface IUserRepository
    {
        Task<AtUser> GetAsync(long userId);
    }
}
