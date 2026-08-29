using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Viv.Elysia.Request
{
    public class ApiIdRequest : VivApiRequest
    {
        /// <summary>
        /// ID标识
        /// </summary>
        [Required]
        [DisplayName("Id")]
        public long Id { get; set; }
    }
}
