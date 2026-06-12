using System;
using System.Collections.Generic;
using System.Text;
using Viv.Momo.Base;
using Viv.Momo.Interface;

namespace Viv.Entity.Database.DeepRed
{
    public class VtUser : EntityBase, ITenant, ISoftDelete
    {
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Phone { get; set; }
        public long TenantId { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
