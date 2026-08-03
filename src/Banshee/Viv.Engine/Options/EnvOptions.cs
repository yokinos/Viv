using System;
using System.Collections.Generic;
using System.Text;
using Viv.Contracts.Enums;

namespace Viv.Engine.Options
{
    public class EnvOptions
    {
        public VivEnv Env { get; set; } 

        public string? ServiceName { get; set; }

        public int MachineId { get; set; }
    }
}
