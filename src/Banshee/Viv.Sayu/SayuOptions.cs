
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Sayu
{
    public class SayuOptions
    {
        public VivSchedulerType SchedulerType { get; set; } = VivSchedulerType.TickerQ;

        public TickerQOptions? TickerQ { get; set; }
    }
}
