using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Contracts.Enums
{
    public enum VivServiceType
    {
        /// <summary>
        /// Web API service.
        /// </summary>
        WebApi = 0,

        /// <summary>
        /// Worker service.
        /// </summary>
        Worker = 1,

        /// <summary>
        /// Gateway service.
        /// </summary>
        Gateway = 2,
    }
}
