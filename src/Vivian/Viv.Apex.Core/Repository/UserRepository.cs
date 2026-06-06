using System;
using System.Collections.Generic;
using System.Text;
using Viv.Apex.Core.IRepository;
using Viv.Contracts.Interface;
using Viv.Momo;
using Viv.Momo.Base;

namespace Viv.Apex.Core.Repository
{
    public class UserRepository : DataAccessBase, IUserRepository
    {
        public UserRepository(IVivContext context, IMomoDbContext dbContext) : base(context, dbContext)
        {

        }


    }
}
