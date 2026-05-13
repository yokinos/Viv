using Viv.Sayu.Enums;
using Viv.Vva.Magic;

namespace Viv.Sayu.Options
{
    public class SayuOptions
    {
        public VivSchedulerType SchedulerType { get; set; } = VivSchedulerType.TickerQ;

        public TickerQOptions? TickerQ { get; set; }

        public List<FilterTypeOptions> TaskTypes { get; set; } = [];
    }
}
